using ListenShelf.Application.Library;
using ListenShelf.Infrastructure.Library;
using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Tests;

public sealed class AudiobookImportTests
{
    [Fact]
    public void Import_ReportsActualCopyAndVerificationBytesBeforeFinalizing()
    {
        using var workspace = new TestWorkspace();
        var data = new byte[3 * 1024 * 1024 + 17];
        new Random(42).NextBytes(data);
        var source = workspace.CreateSourceFile("Progress 日本語.m4b", data);
        var library = CreateLibrary(workspace);
        var updates = new List<LibraryImportProgress>();

        var result = library.Import(source, new InlineProgress<LibraryImportProgress>(updates.Add));

        Assert.Equal(LibraryImportStage.Checking, updates[0].Stage);
        Assert.Equal(LibraryImportStage.Finalizing, updates[^1].Stage);
        foreach (var stage in new[] { LibraryImportStage.Copying, LibraryImportStage.Verifying })
        {
            var phase = updates.Where(update => update.Stage == stage).ToArray();
            Assert.Equal(0, phase[0].ProcessedBytes);
            Assert.Equal(data.Length, phase[^1].ProcessedBytes);
            Assert.All(phase, update => Assert.Equal(data.Length, update.TotalBytes));
            Assert.Equal(phase.OrderBy(update => update.ProcessedBytes), phase);
            Assert.Contains(phase, update => update.ProcessedBytes > 0 && update.ProcessedBytes < data.Length);
        }

        Assert.Equal(updates.OrderBy(update => update.FileFraction), updates);
        Assert.Equal(data, File.ReadAllBytes(source));
        Assert.Equal(data, File.ReadAllBytes(result.Book.FilePath));
        Assert.Empty(Directory.EnumerateFiles(workspace.ManagedLibraryPath, "*.importing", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(LibraryImportStage.Checking, false)]
    [InlineData(LibraryImportStage.Copying, false)]
    [InlineData(LibraryImportStage.Verifying, false)]
    [InlineData(LibraryImportStage.Verifying, true)]
    public void Cancellation_RemovesOnlyTheUnfinishedCopy(LibraryImportStage stage, bool atEnd)
    {
        using var workspace = new TestWorkspace();
        using var cancellation = new CancellationTokenSource();
        var data = new byte[3 * 1024 * 1024 + 1];
        new Random(17).NextBytes(data);
        var source = workspace.CreateSourceFile("Cancel.m4b", data);
        var library = CreateLibrary(workspace);
        var originalTimestamp = File.GetLastWriteTimeUtc(source);
        var updates = new InlineProgress<LibraryImportProgress>(update =>
        {
            if (update.Stage == stage && (stage == LibraryImportStage.Checking
                || (atEnd ? update.ProcessedBytes == data.Length : update.ProcessedBytes > 0)))
            {
                cancellation.Cancel();
            }
        });

        Assert.ThrowsAny<OperationCanceledException>(() => library.Import(source, updates, cancellation.Token));

        Assert.Empty(library.GetBooks());
        AssertNoManagedArtifacts(workspace);
        Assert.Equal(data, File.ReadAllBytes(source));
        Assert.Equal(originalTimestamp, File.GetLastWriteTimeUtc(source));
    }

    [Fact]
    public void AlreadyCanceledToken_DoesNotCreateStorage()
    {
        using var workspace = new TestWorkspace();
        var source = workspace.CreateSourceFile("Not Started.mp3", [1, 2, 3]);
        var library = CreateLibrary(workspace);

        Assert.ThrowsAny<OperationCanceledException>(() =>
            library.Import(source, cancellationToken: new CancellationToken(canceled: true)));
        AssertNoManagedArtifacts(workspace);
        Assert.Empty(library.GetBooks());
    }

    [Fact]
    public void LateCancellation_FinalizesTheVerifiedBookInsteadOfDeletingIt()
    {
        using var workspace = new TestWorkspace();
        using var cancellation = new CancellationTokenSource();
        var source = workspace.CreateSourceFile("Keep completed.m4a", [4, 5, 6]);
        var library = CreateLibrary(workspace);

        var result = library.Import(source, new InlineProgress<LibraryImportProgress>(update =>
        {
            if (update.Stage == LibraryImportStage.Finalizing)
            {
                cancellation.Cancel();
            }
        }), cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(result.WasAdded);
        Assert.Equal(result.Book.Id, Assert.Single(library.GetBooks()).Id);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(result.Book.FilePath));
    }

    [Fact]
    public void VerificationFailure_RemovesTheBadCopyAndPreservesTheOriginal()
    {
        using var workspace = new TestWorkspace();
        var original = new byte[] { 1, 2, 3, 4 };
        var source = workspace.CreateSourceFile("Verified.m4b", original);
        var library = CreateLibrary(workspace);

        var exception = Assert.Throws<IOException>(() => library.Import(source,
            new InlineProgress<LibraryImportProgress>(update =>
            {
                if (update.Stage == LibraryImportStage.Verifying && update.ProcessedBytes == 0)
                {
                    var temporary = Assert.Single(Directory.EnumerateFiles(
                        workspace.ManagedLibraryPath, "*.importing", SearchOption.AllDirectories));
                    File.WriteAllBytes(temporary, [9, 8, 7, 6]);
                }
            })));

        Assert.Contains("SHA-256", exception.Message);
        Assert.Empty(library.GetBooks());
        AssertNoManagedArtifacts(workspace);
        Assert.Equal(original, File.ReadAllBytes(source));
    }

    [Fact]
    public void CatalogFailure_CleansTheVerifiedButUncatalogedCopy()
    {
        using var workspace = new TestWorkspace();
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TRIGGER reject_import BEFORE INSERT ON library_books
            BEGIN SELECT RAISE(ABORT, 'Catalog is read-only for this test'); END;
            """;
        command.ExecuteNonQuery();
        var library = new SqliteAudiobookLibrary(database, workspace.ManagedLibraryPath);
        var source = workspace.CreateSourceFile("Catalog failure.mp3", [1, 2, 3]);

        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => library.Import(source));

        Assert.Empty(library.GetBooks());
        AssertNoManagedArtifacts(workspace);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(source));
    }

    [Fact]
    public void CleanupFailure_ReportsStorageCareInsteadOfClaimingSuccessfulCancellation()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // Windows denies deleting a file held without FileShare.Delete.
        }

        using var workspace = new TestWorkspace();
        using var cancellation = new CancellationTokenSource();
        var source = workspace.CreateSourceFile("Locked.m4b", [1, 2, 3]);
        var library = CreateLibrary(workspace);
        FileStream? heldFile = null;
        try
        {
            var exception = Assert.Throws<IOException>(() => library.Import(source,
                new InlineProgress<LibraryImportProgress>(update =>
                {
                    if (update.Stage == LibraryImportStage.Verifying && update.ProcessedBytes == 0)
                    {
                        var temporary = Assert.Single(Directory.EnumerateFiles(
                            workspace.ManagedLibraryPath, "*.importing", SearchOption.AllDirectories));
                        heldFile = new FileStream(temporary, FileMode.Open, FileAccess.Read, FileShare.Read);
                        cancellation.Cancel();
                    }
                }), cancellation.Token));

            Assert.Contains("Cleanup could not finish", exception.Message);
            Assert.Contains("Storage Care", exception.Message);
            Assert.Empty(library.GetBooks());
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(source));
        }
        finally
        {
            heldFile?.Dispose();
        }
    }

    private static SqliteAudiobookLibrary CreateLibrary(TestWorkspace workspace) =>
        new(new ListenShelfDatabase(workspace.DatabasePath), workspace.ManagedLibraryPath);

    private static void AssertNoManagedArtifacts(TestWorkspace workspace)
    {
        if (Directory.Exists(workspace.ManagedLibraryPath))
        {
            Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.ManagedLibraryPath));
        }
    }
}

internal sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
