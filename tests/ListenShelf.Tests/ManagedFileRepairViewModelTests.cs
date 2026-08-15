using System.Collections.Concurrent;
using ListenShelf.Application.Library;
using ListenShelf.Desktop.ViewModels;

namespace ListenShelf.Tests;

public sealed class ManagedFileRepairViewModelTests
{
    [Fact]
    public async Task ChoosingSourceRequiresExplicitConfirmation_AndChangingBooksClearsIt()
    {
        var first = Book("Alpha");
        var second = Book("Beta");
        var calls = 0;
        var prepared = new List<Guid>();
        var finished = new List<ManagedFileRepairResult?>();
        var model = new ManagedFileRepairViewModel(new StubRepairer((id, source, _, _) =>
        {
            calls++;
            Assert.Equal(second.Id, id);
            Assert.Equal("source.m4b", source);
            Assert.Equal([second.Id], prepared);
            return new ManagedFileRepairResult(id, second.Title, second.FilePath, "retained.repair-backup");
        }), () => Task.FromResult<string?>("source.m4b"), preparePlayback: book => prepared.Add(book.Id),
            finishOperation: result => { finished.Add(result); return Task.CompletedTask; }, dispatch: action => action());
        model.UpdateBooks([second, first]);
        Assert.Equal(["Alpha", "Beta"], model.Books.Select(book => book.Title));
        Assert.False(model.CanChooseSource);
        model.SelectedBook = model.Books[0];
        await model.ChooseSourceCommand.ExecuteAsync(null);
        Assert.True(model.HasSource);
        Assert.Empty(prepared);
        Assert.Equal(0, calls);
        model.SelectedBook = model.Books[1];
        Assert.False(model.HasSource);
        Assert.False(model.CanRepair);
        await model.ChooseSourceCommand.ExecuteAsync(null);
        await model.ConfirmRepairCommand.ExecuteAsync(null);
        Assert.Equal(1, calls);
        Assert.Single(finished);
        Assert.False(model.HasSource);
        Assert.Contains("previous copy is retained", model.StatusText);
        Assert.False(model.IsRunning);
    }

    [Fact]
    public async Task CanceledPickerAndDeniedBusyGate_DoNotRepairOrUnloadPlayback()
    {
        var begins = 0;
        var ends = 0;
        var pickAllowed = true;
        var model = new ManagedFileRepairViewModel(new StubRepairer((_, _, _, _) => throw new InvalidOperationException("Must not run")),
            () => Task.FromResult<string?>(null), () => { begins++; return pickAllowed; }, () => ends++,
            _ => throw new InvalidOperationException("Must not unload"), dispatch: action => action());
        model.UpdateBooks([Book("Book")]);
        model.SelectedBook = model.Books[0];
        await model.ChooseSourceCommand.ExecuteAsync(null);
        Assert.False(model.HasSource);
        Assert.Equal(1, ends);
        model.SourcePath = "source";
        pickAllowed = false;
        await model.ConfirmRepairCommand.ExecuteAsync(null);
        Assert.False(model.IsRunning);
        Assert.Equal(2, begins);
        Assert.Equal(1, ends);
    }

    [Fact]
    public async Task FailureReleasesBusyGate_AndStillRefreshesState()
    {
        var finished = false;
        var ended = false;
        var model = new ManagedFileRepairViewModel(new StubRepairer((_, _, _, _) => throw new IOException("Fingerprint mismatch")),
            () => Task.FromResult<string?>(null), endOperation: () => ended = true,
            finishOperation: result => { Assert.Null(result); finished = true; return Task.CompletedTask; }, dispatch: action => action());
        model.UpdateBooks([Book("Book")]);
        model.SelectedBook = model.Books[0];
        model.SourcePath = "source";
        await model.ConfirmRepairCommand.ExecuteAsync(null);
        Assert.True(ended);
        Assert.True(finished);
        Assert.Contains("Fingerprint mismatch", model.StatusText);
        Assert.True(model.CanRepair);
    }

    [Fact]
    public async Task CloseCancelsAndWaitsThroughCleanupAndRefresh()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var ended = false;
        var model = new ManagedFileRepairViewModel(new StubRepairer((_, _, progress, token) =>
        {
            progress?.Report(new LibraryImportProgress(LibraryImportStage.Copying, 5, 10));
            started.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Should have been canceled");
        }), () => Task.FromResult<string?>(null), endOperation: () => ended = true,
            finishOperation: async result => { Assert.Null(result); refreshStarted.TrySetResult(); await refreshComplete.Task; },
            dispatch: action => action());
        model.UpdateBooks([Book("Book")]);
        model.SelectedBook = model.Books[0];
        model.SourcePath = "source";
        var run = model.ConfirmRepairCommand.ExecuteAsync(null);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(50, model.Percentage);
            Assert.False(model.CanRepair);
            Assert.False(model.CanChooseSource);
            var close = model.CancelAndWaitAsync();
            Assert.False(close.IsCompleted);
            Assert.False(ended);
            release.Set();
            await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(model.IsRunning);
            Assert.False(close.IsCompleted);
            refreshComplete.SetResult();
            await close.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(ended);
            Assert.False(model.IsRunning);
            Assert.Contains("canceled before replacement", model.StatusText);
        }
        finally
        {
            release.Set();
            refreshComplete.TrySetResult();
            await run.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task FinalizationCannotBeCanceled_AndDelayedProgressCannotChangeCompletedState()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var pending = new ConcurrentQueue<Action>();
        var model = new ManagedFileRepairViewModel(new StubRepairer((id, source, progress, token) =>
        {
            progress?.Report(new LibraryImportProgress(LibraryImportStage.Finalizing));
            started.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            Assert.False(token.IsCancellationRequested);
            return new ManagedFileRepairResult(id, "Book", source, null);
        }), () => Task.FromResult<string?>(null), dispatch: pending.Enqueue);
        model.UpdateBooks([Book("Book")]);
        model.SelectedBook = model.Books[0];
        model.SourcePath = "source";
        var run = model.ConfirmRepairCommand.ExecuteAsync(null);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(pending.TryDequeue(out var finalProgress));
            finalProgress();
            Assert.True(model.IsFinalizing);
            Assert.False(model.CanCancel);
            var close = model.CancelAndWaitAsync();
            release.Set();
            await close.WaitAsync(TimeSpan.FromSeconds(10));
            var status = model.StatusText;
            finalProgress();
            Assert.Equal(status, model.StatusText);
            Assert.False(model.IsCancellationRequested);
        }
        finally { release.Set(); await run.WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    [Fact]
    public async Task RepairResolvesOnlyItsOwnVerificationFinding()
    {
        var first = Book("First");
        var second = Book("Second");
        var verification = new ManagedFileVerificationViewModel(new ProblemVerifier([first, second]), dispatch: action => action());
        verification.UpdateBooks([first, second]);
        await verification.VerifyAllCommand.ExecuteAsync(null);
        verification.RecordRepair(new ManagedFileRepairResult(first.Id, first.Title, first.FilePath, null));
        Assert.Equal(second.Id, Assert.Single(verification.KnownProblems).BookId);
        Assert.Equal(ManagedFileVerificationStatus.Unchanged, verification.Results[0].Status);
        Assert.True(verification.HasProblems);
    }

    private static LibraryBook Book(string title) => new(Guid.NewGuid(), new AudiobookMetadata { Title = title }, title + ".m4b", 10, DateTimeOffset.UtcNow);

    private sealed class StubRepairer(Func<Guid, string, IProgress<LibraryImportProgress>?, CancellationToken, ManagedFileRepairResult> repair) : IManagedFileRepairer
    {
        public ManagedFileRepairResult Repair(Guid bookId, string sourceFilePath, IProgress<LibraryImportProgress>? progress = null,
            CancellationToken cancellationToken = default) => repair(bookId, sourceFilePath, progress, cancellationToken);
    }

    private sealed class ProblemVerifier(LibraryBook[] books) : IManagedFileVerifier
    {
        public ManagedFileVerificationReport Verify(Guid? bookId = null, IProgress<ManagedFileVerificationProgress>? progress = null,
            CancellationToken cancellationToken = default) => new(DateTimeOffset.UtcNow, books.Select(book =>
                new ManagedFileVerificationResult(book.Id, book.Title, book.FilePath, ManagedFileVerificationStatus.Missing, "Missing", DateTimeOffset.UtcNow)).ToArray());
    }
}
