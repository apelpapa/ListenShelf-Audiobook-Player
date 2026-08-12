using ListenShelf.Application.Library;
using ListenShelf.Infrastructure.Library;
using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Tests;

public sealed class LibraryImportBatchTests
{
    [Fact]
    public void MixedBatch_ReportsAddedDuplicateAndFailedFilesAndContinues()
    {
        using var workspace = new TestWorkspace();
        var library = CreateLibrary(workspace);
        var existing = workspace.CreateSourceFile("Existing.mp3", [1]);
        library.Import(existing);
        var first = workspace.CreateSourceFile("First.m4b", [2, 3]);
        var unsupported = workspace.CreateSourceFile("Unsupported.txt", [4]);
        var last = workspace.CreateSourceFile("Last.m4a", [5, 6]);
        var updates = new List<LibraryImportBatchProgress>();

        var result = new LibraryImportBatch(library).Run([first, existing, unsupported, last],
            new InlineProgress<LibraryImportBatchProgress>(updates.Add));

        Assert.Equal(2, result.AddedCount);
        Assert.Equal(1, result.ExistingCount);
        Assert.Equal(1, result.FailedCount);
        Assert.False(result.WasCanceled);
        Assert.Equal(3, library.GetBooks().Count);
        Assert.Equal([first, existing, unsupported, last], result.Files.Select(file => file.FilePath));
        Assert.Contains("M4B, M4A, and MP3", result.Files[2].Message);
        Assert.Equal(4, updates.Count(update => update.CompletedFile is not null));
        Assert.All(updates, update => Assert.InRange(update.OverallPercentage, 0, 100));
        Assert.Equal(100d, updates[^1].OverallPercentage);
        Assert.True(File.Exists(unsupported));
    }

    [Theory]
    [InlineData(LibraryImportStage.Copying)]
    [InlineData(LibraryImportStage.Verifying)]
    public void Cancellation_KeepsEarlierBooksAndDoesNotProcessRemainingFiles(LibraryImportStage stage)
    {
        using var workspace = new TestWorkspace();
        using var cancellation = new CancellationTokenSource();
        var library = CreateLibrary(workspace);
        var first = workspace.CreateSourceFile("First.m4b", [1]);
        var second = workspace.CreateSourceFile("Second.mp3", new byte[2 * 1024 * 1024]);
        var third = workspace.CreateSourceFile("Third.m4a", [3]);

        var result = new LibraryImportBatch(library).Run([first, second, third],
            new InlineProgress<LibraryImportBatchProgress>(update =>
            {
                if (update.FileNumber == 2 && update.FileProgress.Stage == stage
                    && update.FileProgress.ProcessedBytes > 0)
                {
                    cancellation.Cancel();
                }
            }), cancellation.Token);

        Assert.True(result.WasCanceled);
        Assert.Equal(1, result.AddedCount);
        Assert.Equal(1, result.CanceledCount);
        Assert.Equal(1, result.NotProcessedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal("First", Assert.Single(library.GetBooks()).Title);
        Assert.Single(Directory.EnumerateDirectories(workspace.ManagedLibraryPath));
        Assert.Empty(Directory.EnumerateFiles(workspace.ManagedLibraryPath, "*.importing", SearchOption.AllDirectories));
        Assert.All(new[] { first, second, third }, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public void CancellationDuringFinalization_CountsTheFinishedImportAsAdded()
    {
        using var workspace = new TestWorkspace();
        using var cancellation = new CancellationTokenSource();
        var library = CreateLibrary(workspace);
        var first = workspace.CreateSourceFile("Finished.m4b", [1]);
        var second = workspace.CreateSourceFile("Not started.mp3", [2]);

        var result = new LibraryImportBatch(library).Run([first, second],
            new InlineProgress<LibraryImportBatchProgress>(update =>
            {
                if (update.FileProgress.Stage == LibraryImportStage.Finalizing)
                {
                    cancellation.Cancel();
                }
            }), cancellation.Token);

        Assert.Equal(1, result.AddedCount);
        Assert.Equal(0, result.CanceledCount);
        Assert.Equal(1, result.NotProcessedCount);
        Assert.Equal("Finished", Assert.Single(library.GetBooks()).Title);
    }

    [Fact]
    public void CanceledBeforeStarting_ReportsEveryFileAsNotProcessed()
    {
        using var workspace = new TestWorkspace();
        var library = CreateLibrary(workspace);
        var result = new LibraryImportBatch(library).Run(["one.m4b", "two.mp3"],
            cancellationToken: new CancellationToken(canceled: true));

        Assert.True(result.WasCanceled);
        Assert.Equal(2, result.NotProcessedCount);
        Assert.Empty(library.GetBooks());
        Assert.False(Directory.Exists(workspace.ManagedLibraryPath));
    }

    private static SqliteAudiobookLibrary CreateLibrary(TestWorkspace workspace) =>
        new(new ListenShelfDatabase(workspace.DatabasePath), workspace.ManagedLibraryPath);
}
