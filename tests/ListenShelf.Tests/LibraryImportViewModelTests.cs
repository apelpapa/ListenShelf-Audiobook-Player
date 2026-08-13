using System.Collections.Concurrent;
using ListenShelf.Application.Library;
using ListenShelf.Desktop.ViewModels;
using ListenShelf.Infrastructure.Library;
using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Tests;

public sealed class LibraryImportViewModelTests
{
    [Fact]
    public async Task CompletedBatch_ShowsSummaryAndPerFileResultsUntilDismissed()
    {
        using var workspace = new TestWorkspace();
        var library = CreateLibrary(workspace);
        var book = workspace.CreateSourceFile("Book.m4b", [1, 2]);
        var unsupported = workspace.CreateSourceFile("Notes.txt", [3]);
        var model = new LibraryImportViewModel(library, action => action());

        var result = await model.ImportAsync([book, book, unsupported]);

        Assert.Equal(result.Summary, model.StatusText);
        Assert.Equal(["Added", "Already in library", "Failed"], model.Results.Select(item => item.StatusText));
        Assert.True(model.HasResults);
        Assert.True(model.AreDetailsExpanded);
        Assert.True(model.IsVisible);
        Assert.False(model.IsRunning);
        Assert.False(model.CancelCommand.CanExecute(null));
        Assert.True(model.DismissCommand.CanExecute(null));
        Assert.Equal(100d, model.OverallPercentage);

        model.DismissCommand.Execute(null);
        Assert.False(model.IsVisible);
        Assert.Single(library.GetBooks());
    }

    [Theory]
    [InlineData(LibraryImportStage.Copying, false)]
    [InlineData(LibraryImportStage.Verifying, false)]
    [InlineData(LibraryImportStage.Finalizing, true)]
    public async Task CancelAndWait_WaitsForCleanupOrFinalizationBeforeReturning(
        LibraryImportStage stage, bool shouldKeepBook)
    {
        using var workspace = new TestWorkspace();
        var source = workspace.CreateSourceFile("Book.m4b", new byte[2 * 1024 * 1024]);
        var next = workspace.CreateSourceFile("Next.mp3", [4]);
        using var library = new GatedLibrary(CreateLibrary(workspace), source, stage);
        var model = new LibraryImportViewModel(library, action => action());
        var import = model.ImportAsync([source, next]);

        try
        {
            await library.ReachedGate.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(model.IsRunning);
            Assert.True(model.CancelCommand.CanExecute(null));
            Assert.False(model.DismissCommand.CanExecute(null));
            Assert.Equal("Book.m4b", model.CurrentFileName);
            Assert.Equal("Book 1 of 2", model.BookNumberText);
            Assert.Equal(stage is LibraryImportStage.Finalizing, model.IsStageIndeterminate);
            if (stage is not LibraryImportStage.Finalizing)
            {
                Assert.Contains("of 2 MB", model.ByteProgressText);
            }

            Assert.Throws<InvalidOperationException>(() => { _ = model.ImportAsync([next]); });
            model.DismissCommand.Execute(null);
            Assert.True(model.IsVisible);

            var closeWait = model.CancelAndWaitAsync();
            Assert.False(closeWait.IsCompleted);
            Assert.True(model.IsCancellationRequested);
            Assert.False(model.CancelCommand.CanExecute(null));
            Assert.Contains("Canceling safely", model.StatusText);

            library.Release();
            await closeWait.WaitAsync(TimeSpan.FromSeconds(10));
            var result = await import;

            Assert.False(model.IsRunning);
            Assert.True(model.CanDismiss);
            Assert.True(model.AreDetailsExpanded);
            Assert.Equal(shouldKeepBook ? 1 : 0, result.AddedCount);
            Assert.Equal(shouldKeepBook ? 0 : 1, result.CanceledCount);
            Assert.Equal(1, result.NotProcessedCount);
            Assert.Equal(result.Summary, model.StatusText);
            Assert.Equal(shouldKeepBook ? 1 : 0, library.GetBooks().Count);
            Assert.Empty(Directory.EnumerateFiles(workspace.ManagedLibraryPath, "*.importing", SearchOption.AllDirectories));
            Assert.Equal(shouldKeepBook ? 1 : 0, Directory.EnumerateDirectories(workspace.ManagedLibraryPath).Count());
            Assert.Equal(2 * 1024 * 1024, new FileInfo(source).Length);
            Assert.True(File.Exists(next));
        }
        finally
        {
            library.Release();
            await import.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Theory]
    [InlineData(LibraryImportStage.Fingerprinting, "Checking audiobook fingerprint")]
    [InlineData(LibraryImportStage.Comparing, "Comparing with Existing")]
    public async Task DuplicateChecks_ShowProgressAndRemainCancelable(LibraryImportStage stage, string label)
    {
        using var workspace = new TestWorkspace();
        var inner = CreateLibrary(workspace);
        var bytes = new byte[2 * 1024 * 1024];
        inner.Import(workspace.CreateSourceFile("Existing.m4b", bytes));
        var source = workspace.CreateSourceFile("Renamed.m4b", bytes);
        using var library = new GatedLibrary(inner, source, stage);
        var model = new LibraryImportViewModel(library, action => action());
        var import = model.ImportAsync([source]);
        try
        {
            await library.ReachedGate.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(label, model.StageText);
            Assert.Contains("of 2 MB", model.ByteProgressText);
            Assert.False(model.IsStageIndeterminate);
            var cancel = model.CancelAndWaitAsync();
            library.Release();
            await cancel.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, (await import).CanceledCount);
            Assert.Single(inner.GetBooks());
            Assert.True(File.Exists(source));
        }
        finally
        {
            library.Release();
            await import.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task DelayedProgress_CannotOverwriteCompletedResultsOrTheNextBatch()
    {
        using var workspace = new TestWorkspace();
        var first = workspace.CreateSourceFile("First.m4b", [1]);
        var second = workspace.CreateSourceFile("Second.mp3", [2]);
        using var library = new GatedLibrary(CreateLibrary(workspace), second, LibraryImportStage.Copying);
        var callbacks = new ConcurrentQueue<Action>();
        var model = new LibraryImportViewModel(library, callbacks.Enqueue);

        var firstResult = await model.ImportAsync([first]);
        var oldCallbacks = callbacks.ToArray();
        callbacks.Clear();
        Assert.NotEmpty(oldCallbacks);
        Assert.False(model.AreDetailsExpanded);
        foreach (var callback in oldCallbacks)
        {
            callback();
        }

        Assert.Equal(firstResult.Summary, model.StatusText);
        Assert.Single(model.Results);
        Assert.Empty(model.CurrentFileName);

        var secondImport = model.ImportAsync([second]);
        try
        {
            await library.ReachedGate.WaitAsync(TimeSpan.FromSeconds(10));
            foreach (var callback in oldCallbacks)
            {
                callback();
            }

            Assert.Empty(model.Results);
            Assert.Empty(model.CurrentFileName);
            Assert.Equal("Preparing…", model.StageText);

            var cancel = model.CancelAndWaitAsync();
            while (callbacks.TryDequeue(out var callback))
            {
                callback();
            }

            Assert.Contains("Canceling safely", model.StatusText);
            library.Release();
            await cancel.WaitAsync(TimeSpan.FromSeconds(10));
            var result = await secondImport;
            while (callbacks.TryDequeue(out var callback))
            {
                callback();
            }

            Assert.Equal(result.Summary, model.StatusText);
            Assert.Equal("Canceled", Assert.Single(model.Results).StatusText);
            Assert.Equal("First", Assert.Single(library.GetBooks()).Title);
        }
        finally
        {
            library.Release();
            await secondImport.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static SqliteAudiobookLibrary CreateLibrary(TestWorkspace workspace) =>
        new(new ListenShelfDatabase(workspace.DatabasePath), workspace.ManagedLibraryPath);

    private sealed class GatedLibrary(
        IAudiobookLibrary inner, string pausedPath, LibraryImportStage pausedStage) : IAudiobookLibrary, IDisposable
    {
        private readonly ManualResetEventSlim _release = new(false);
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _hasPaused;

        public Task ReachedGate => _reached.Task;
        public string ManagedLibraryPath => inner.ManagedLibraryPath;
        public void Release() => _release.Set();
        public void Dispose() => _release.Dispose();

        public LibraryImportResult Import(string sourceFilePath, IProgress<LibraryImportProgress>? progress = null,
            CancellationToken cancellationToken = default) => inner.Import(sourceFilePath,
                new InlineProgress<LibraryImportProgress>(update =>
                {
                    progress?.Report(update);
                    if (_hasPaused || sourceFilePath != pausedPath || update.Stage != pausedStage)
                    {
                        return;
                    }

                    _hasPaused = true;
                    _reached.TrySetResult();
                    // Simulate an in-flight disk operation. Cancellation must wait
                    // for it to return before the real importer cleans up or saves.
                    if (!_release.Wait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException("The test did not release the import gate.");
                    }
                }), cancellationToken);

        public IReadOnlyList<LibraryBook> GetBooks() => inner.GetBooks();
        public LibraryRemovalResult Remove(Guid bookId) => inner.Remove(bookId);
        public LibraryBook SetCover(Guid bookId, string sourceImagePath) => inner.SetCover(bookId, sourceImagePath);
        public LibraryBook SetCover(Guid bookId, ReadOnlyMemory<byte> imageData, string fileExtension) =>
            inner.SetCover(bookId, imageData, fileExtension);
        public LibraryBook UpdateMetadata(Guid bookId, AudiobookMetadata metadata) => inner.UpdateMetadata(bookId, metadata);
    }
}
