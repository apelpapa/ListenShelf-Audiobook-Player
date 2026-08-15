using System.Security.Cryptography;
using ListenShelf.Application.Bookmarks;
using ListenShelf.Application.Library;
using ListenShelf.Application.Progress;
using ListenShelf.Infrastructure.Bookmarks;
using ListenShelf.Infrastructure.Library;
using ListenShelf.Infrastructure.Progress;
using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Tests;

public sealed class ContentDuplicateTests
{
    [Theory]
    [InlineData("Renamed.m4b")]
    [InlineData("Renamed.m4a")]
    [InlineData("Renamed.mp3")]
    public void IdenticalContentInAnotherFolder_ReusesBookAndPreservesAllListeningData(string name)
    {
        using var workspace = new TestWorkspace();
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        var library = new SqliteAudiobookLibrary(database, workspace.ManagedLibraryPath);
        byte[] bytes = [1, 2, 3, 4, 5];
        var source = workspace.CreateSourceFile("Original.m4b", bytes);
        var book = library.Import(source).Book;
        library.UpdateMetadata(book.Id, new AudiobookMetadata { Title = "My edited title", Authors = ["Author"] });
        var cover = workspace.CreateSourceFile("Cover.png", [9, 8, 7]);
        library.SetCover(book.Id, cover);
        var progress = new PlaybackProgress(book.FilePath, TimeSpan.FromMinutes(5), TimeSpan.FromHours(3), DateTimeOffset.UtcNow);
        var progressStore = new SqlitePlaybackProgressStore(database);
        progressStore.Save(progress);
        var bookmark = new PlaybackBookmark(Guid.NewGuid(), book.FilePath, TimeSpan.FromMinutes(4),
            "Saved", "My note", 0, "Chapter 1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var bookmarks = new SqlitePlaybackBookmarkStore(database);
        bookmarks.Save(bookmark);
        var saved = Assert.Single(library.GetBooks());
        var otherFolder = Path.Combine(Path.GetDirectoryName(source)!, "Other folder 日本語");
        Directory.CreateDirectory(otherFolder);
        var renamed = Path.Combine(otherFolder, name);
        File.Copy(source, renamed);
        var updates = new List<LibraryImportProgress>();

        var result = library.Import(renamed, new InlineProgress<LibraryImportProgress>(updates.Add));

        Assert.False(result.WasAdded);
        Assert.Equal(saved.Id, result.Book.Id);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(saved.Metadata),
            System.Text.Json.JsonSerializer.Serialize(result.Book.Metadata));
        Assert.Equal(saved.CoverPath, result.Book.CoverPath);
        Assert.Equal(progress, progressStore.Get(book.FilePath));
        Assert.Equal(bookmark, Assert.Single(bookmarks.GetForFile(book.FilePath)));
        Assert.Equal(new byte[] { 9, 8, 7 }, File.ReadAllBytes(result.Book.CoverPath!));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), ReadHash(database, book.Id));
        Assert.Contains(updates, update => update.Stage == LibraryImportStage.Fingerprinting);
        Assert.Contains(updates, update => update.Stage == LibraryImportStage.Comparing);
        Assert.DoesNotContain(updates, update => update.Stage == LibraryImportStage.Copying);
        Assert.Single(library.GetBooks());
        Assert.Single(Directory.EnumerateDirectories(workspace.ManagedLibraryPath));
        Assert.Equal(bytes, File.ReadAllBytes(source));
        Assert.Equal(bytes, File.ReadAllBytes(renamed));
    }

    [Fact]
    public void SameSourcePathWithDifferentBytes_AddsAnotherBookInsteadOfSkippingIt()
    {
        using var workspace = new TestWorkspace();
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        var library = new SqliteAudiobookLibrary(database, workspace.ManagedLibraryPath);
        var source = workspace.CreateSourceFile("Same title.mp3", [1, 2, 3]);
        var first = library.Import(source).Book;
        File.WriteAllBytes(source, [3, 2, 1]);

        var second = library.Import(source);

        Assert.True(second.WasAdded);
        Assert.NotEqual(first.Id, second.Book.Id);
        Assert.Equal(first.Title, second.Book.Title);
        Assert.NotEqual(ReadHash(database, first.Id), ReadHash(database, second.Book.Id));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(first.FilePath));
        Assert.Equal(new byte[] { 3, 2, 1 }, File.ReadAllBytes(second.Book.FilePath));
        Assert.Equal(2, library.GetBooks().Count);
        Assert.Equal(second.Book.Id, library.Import(source).Book.Id);
    }

    [Fact]
    public void LegacyFingerprints_AreFilledOnlyForSameSizedCandidatesAndPersistAcrossReopen()
    {
        using var workspace = new TestWorkspace();
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        var library = new SqliteAudiobookLibrary(database, workspace.ManagedLibraryPath);
        var source = workspace.CreateSourceFile("Legacy.m4b", [1, 2, 3]);
        var old = library.Import(source).Book;
        var unrelated = library.Import(workspace.CreateSourceFile("Other.mp3", [7, 8])).Book;
        Execute(database, "UPDATE library_books SET content_sha256 = NULL;");
        var duplicate = workspace.CreateSourceFile("Renamed.m4b", [1, 2, 3]);
        var updates = new List<LibraryImportProgress>();

        Assert.Equal(old.Id, library.Import(duplicate, new InlineProgress<LibraryImportProgress>(updates.Add)).Book.Id);

        Assert.NotNull(ReadHash(database, old.Id));
        Assert.Null(ReadHash(database, unrelated.Id));
        Assert.DoesNotContain(updates, update => update.Detail == unrelated.Title);
        var reopened = new SqliteAudiobookLibrary(new ListenShelfDatabase(workspace.DatabasePath), workspace.ManagedLibraryPath);
        Assert.False(reopened.Import(duplicate).WasAdded);
        Assert.Equal(2, reopened.GetBooks().Count);
    }

    [Theory]
    [InlineData(LibraryImportStage.Fingerprinting)]
    [InlineData(LibraryImportStage.Comparing)]
    public void CancelingDuplicateChecks_LeavesExistingDataAndOriginalsIntact(LibraryImportStage stage)
    {
        using var workspace = new TestWorkspace();
        using var cancellation = new CancellationTokenSource();
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        var library = new SqliteAudiobookLibrary(database, workspace.ManagedLibraryPath);
        var bytes = new byte[2 * 1024 * 1024];
        new Random(3).NextBytes(bytes);
        var source = workspace.CreateSourceFile("Existing.m4b", bytes);
        var old = library.Import(source).Book;
        Execute(database, "UPDATE library_books SET content_sha256 = NULL;");
        var renamed = workspace.CreateSourceFile("Renamed.m4b", bytes);

        Assert.ThrowsAny<OperationCanceledException>(() => library.Import(renamed,
            new InlineProgress<LibraryImportProgress>(update =>
            {
                if (update.Stage == stage && update.ProcessedBytes > 0)
                {
                    cancellation.Cancel();
                }
            }), cancellation.Token));

        Assert.Equal(old.Id, Assert.Single(library.GetBooks()).Id);
        Assert.Null(ReadHash(database, old.Id));
        Assert.Single(Directory.EnumerateDirectories(workspace.ManagedLibraryPath));
        Assert.Equal(bytes, File.ReadAllBytes(old.FilePath));
        Assert.Equal(bytes, File.ReadAllBytes(renamed));
        Assert.Equal(bytes, File.ReadAllBytes(source));
        Assert.False(library.Import(renamed).WasAdded); // Cancellation released the import gate.
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingOrChangedManagedCopy_IsNotReportedAsAHealthyDuplicate(bool missing)
    {
        using var workspace = new TestWorkspace();
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        var library = new SqliteAudiobookLibrary(database, workspace.ManagedLibraryPath);
        var source = workspace.CreateSourceFile("Book.m4b", [1, 2, 3]);
        var old = library.Import(source).Book;
        var baseline = ReadHash(database, old.Id);
        if (missing)
        {
            File.Delete(old.FilePath);
        }
        else
        {
            File.WriteAllBytes(old.FilePath, [9, 8, 7]);
        }

        var exception = Assert.Throws<IOException>(() => library.Import(source));

        Assert.Contains(old.Title, exception.Message);
        Assert.Contains("Storage Care", exception.Message);
        Assert.Equal(baseline, ReadHash(database, old.Id));
        Assert.Single(library.GetBooks());
        Assert.Single(Directory.EnumerateDirectories(workspace.ManagedLibraryPath));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(source));
        if (!missing)
        {
            Assert.Equal(new byte[] { 9, 8, 7 }, File.ReadAllBytes(old.FilePath));
        }
    }

    [Fact]
    public void ExistingDuplicateEntries_AreNotMergedOrDeleted()
    {
        using var workspace = new TestWorkspace();
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        var library = new SqliteAudiobookLibrary(database, workspace.ManagedLibraryPath);
        var first = library.Import(workspace.CreateSourceFile("First.mp3", [1, 2, 3])).Book;
        var second = library.Import(workspace.CreateSourceFile("Second.mp3", [3, 2, 1])).Book;
        File.WriteAllBytes(second.FilePath, [1, 2, 3]);
        Execute(database, "UPDATE library_books SET content_sha256 = NULL;");

        var result = library.Import(workspace.CreateSourceFile("Third.mp3", [1, 2, 3]));

        Assert.False(result.WasAdded);
        Assert.Equal(2, library.GetBooks().Count);
        Assert.True(File.Exists(first.FilePath));
        Assert.True(File.Exists(second.FilePath));
        Assert.Equal(2, Directory.EnumerateDirectories(workspace.ManagedLibraryPath).Count());
    }

    [Fact]
    public void BatchSummary_NamesTheExistingBookForARenamedDuplicate()
    {
        using var workspace = new TestWorkspace();
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        var library = new SqliteAudiobookLibrary(database, workspace.ManagedLibraryPath);
        var first = workspace.CreateSourceFile("Original title.m4b", [1, 2]);
        var duplicate = workspace.CreateSourceFile("Different name.m4b", [1, 2]);

        var result = new LibraryImportBatch(library).Run([first, duplicate]);

        Assert.Equal(1, result.AddedCount);
        Assert.Equal(1, result.ExistingCount);
        Assert.Contains("Original title", result.Files[1].Message);
        Assert.Contains("no extra copy", result.Files[1].Message);
        Assert.Single(library.GetBooks());
    }

    [Fact]
    public async Task ConcurrentLibraryInstances_CannotBothImportTheSameContent()
    {
        using var workspace = new TestWorkspace();
        using var release = new ManualResetEventSlim();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        var firstLibrary = new SqliteAudiobookLibrary(database, workspace.ManagedLibraryPath);
        var secondLibrary = new SqliteAudiobookLibrary(database, workspace.ManagedLibraryPath);
        var first = workspace.CreateSourceFile("First.m4b", [1, 2, 3]);
        var second = workspace.CreateSourceFile("Second.m4b", [1, 2, 3]);
        var firstImport = Task.Run(() => firstLibrary.Import(first, new InlineProgress<LibraryImportProgress>(update =>
        {
            if (update.Stage == LibraryImportStage.Finalizing)
            {
                reached.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("Import gate was not released.");
                }
            }
        })));
        Task<LibraryImportResult>? secondImport = null;
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var cancellation = new CancellationTokenSource();
            var waitingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var waiting = Task.Run(() => secondLibrary.Import(second, new InlineProgress<LibraryImportProgress>(update =>
            {
                if (update.Detail == "Waiting for another library operation to finish")
                {
                    waitingStarted.TrySetResult();
                }
            }), cancellation.Token));
            await waitingStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
            var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            secondImport = Task.Run(() => secondLibrary.Import(second, new InlineProgress<LibraryImportProgress>(update =>
            {
                if (update.Detail == "Waiting for another library operation to finish")
                {
                    secondStarted.TrySetResult();
                }
            })));
            await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            release.Set();
            var results = await Task.WhenAll(firstImport, secondImport).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Single(results, result => result.WasAdded);
            Assert.Equal(results[0].Book.Id, results[1].Book.Id);
            Assert.Single(firstLibrary.GetBooks());
            Assert.Single(Directory.EnumerateDirectories(workspace.ManagedLibraryPath));
        }
        finally
        {
            release.Set();
            await firstImport.WaitAsync(TimeSpan.FromSeconds(10));
            if (secondImport is not null)
            {
                await secondImport.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
    }

    private static string? ReadHash(ListenShelfDatabase database, Guid bookId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT content_sha256 FROM library_books WHERE book_id = $id;";
        command.Parameters.AddWithValue("$id", bookId.ToString("D"));
        return command.ExecuteScalar() as string;
    }

    private static void Execute(ListenShelfDatabase database, string sql)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
