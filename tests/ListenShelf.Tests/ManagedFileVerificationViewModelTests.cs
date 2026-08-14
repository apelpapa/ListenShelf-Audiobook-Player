using System.Collections.Concurrent;
using ListenShelf.Application.Library;
using ListenShelf.Desktop.ViewModels;

namespace ListenShelf.Tests;

public sealed class ManagedFileVerificationViewModelTests
{
    [Fact]
    public async Task ScansAreManualAndUseTheRequestedScope()
    {
        var a = Book("Alpha");
        var z = Book("Zebra");
        var service = new StubVerifier((id, _, _) => Report((id is null ? new[] { a, z } : new[] { z })
            .Select(book => Result(book, ManagedFileVerificationStatus.Unchanged)).ToArray()));
        var model = new ManagedFileVerificationViewModel(service, dispatch: action => action());
        model.UpdateBooks([z, a]);

        Assert.Empty(service.Calls);
        Assert.False(model.HasResults);
        Assert.False(model.HasProblems);
        Assert.Equal(["Alpha", "Zebra"], model.Books.Select(book => book.Title));
        Assert.False(model.CanVerifySelected);
        Assert.True(model.CanVerifyAll);

        await model.VerifyAllCommand.ExecuteAsync(null);
        Assert.Equal(2, model.Results.Count);
        Assert.Contains("Entire library", model.LastCheckedText);
        model.SelectedBook = model.Books.Single(book => book.Id == z.Id);
        await model.VerifySelectedCommand.ExecuteAsync(null);
        Assert.Equal(new Guid?[] { null, z.Id }, service.Calls);
        Assert.Equal(z.Id, Assert.Single(model.Results).BookId);
        Assert.Contains("Zebra", model.LastCheckedText);
    }

    [Fact]
    public async Task CheckingAnotherBookOrCanceling_DoesNotClearAnOutstandingFinding()
    {
        var a = Book("Changed book");
        var b = Book("Healthy book");
        var status = ManagedFileVerificationStatus.Changed;
        var service = new StubVerifier((id, _, _) => Report(id == b.Id
            ? Result(b, ManagedFileVerificationStatus.Unchanged) : Result(a, status)));
        var model = new ManagedFileVerificationViewModel(service, dispatch: action => action());
        model.UpdateBooks([a, b]);
        await model.VerifyAllCommand.ExecuteAsync(null);
        Assert.True(model.HasProblems);
        model.SelectedBook = model.Books.Single(book => book.Id == b.Id);
        await model.VerifySelectedCommand.ExecuteAsync(null);
        Assert.True(model.HasProblems);
        Assert.Equal(a.Id, Assert.Single(model.KnownProblems).BookId);
        model.SelectedBook = model.Books.Single(book => book.Id == a.Id);
        status = ManagedFileVerificationStatus.Canceled;
        await model.VerifySelectedCommand.ExecuteAsync(null);
        Assert.True(model.HasProblems);
        Assert.Equal(ManagedFileVerificationStatus.Changed, Assert.Single(model.KnownProblems).Status);
        status = ManagedFileVerificationStatus.Unchanged;
        await model.VerifySelectedCommand.ExecuteAsync(null);
        Assert.False(model.HasProblems);
    }

    [Fact]
    public async Task NoBaseline_IsNotDisplayedAsVerifiedOrAsCorruption()
    {
        var book = Book("Older book");
        var model = new ManagedFileVerificationViewModel(new StubVerifier((_, _, _) =>
            Report(Result(book, ManagedFileVerificationStatus.NoBaseline))), dispatch: action => action());
        model.UpdateBooks([book]);
        await model.VerifyAllCommand.ExecuteAsync(null);
        Assert.False(model.HasProblems);
        Assert.Equal("No verification baseline", Assert.Single(model.Results).StatusText);
        Assert.Contains("0 unchanged", model.StatusText);
        Assert.Contains("1 without a baseline", model.StatusText);
    }

    [Fact]
    public async Task CancelAndWait_BlocksCompetingScansAndWaitsForReadHandleRelease()
    {
        var book = Book("Book");
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var begins = 0;
        var ends = 0;
        var service = new StubVerifier((_, progress, token) =>
        {
            progress?.Report(new ManagedFileVerificationProgress(1, 1, book.Title, 5, 10));
            reached.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test gate was not released.");
            return Report(Result(book, token.IsCancellationRequested ? ManagedFileVerificationStatus.Canceled : ManagedFileVerificationStatus.Unchanged));
        });
        var model = new ManagedFileVerificationViewModel(service, () => { begins++; return true; }, () => ends++, action => action());
        model.UpdateBooks([book]);
        var run = model.VerifyAllCommand.ExecuteAsync(null);
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(model.IsRunning);
            Assert.False(model.CanVerifyAll);
            Assert.True(model.CanCancel);
            Assert.Equal(50, model.FilePercentage);
            Assert.Contains("Book 1 of 1", model.CurrentBookText);
            model.SelectedBook = model.Books[0];
            await model.VerifySelectedCommand.ExecuteAsync(null);
            Assert.Single(service.Calls);
            var close = model.CancelAndWaitAsync();
            Assert.False(close.IsCompleted);
            Assert.False(model.CanCancel);
            Assert.Contains("Canceling", model.StatusText);
            Assert.Equal(0, ends);
            release.Set();
            await close.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, begins);
            Assert.Equal(1, ends);
            Assert.False(model.IsRunning);
            Assert.Equal(ManagedFileVerificationStatus.Canceled, Assert.Single(model.Results).Status);
        }
        finally
        {
            release.Set();
            await run.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task FailureOrDeniedOperation_DoesNotLeaveTheLibraryBusy()
    {
        var book = Book("Book");
        var ends = 0;
        var service = new StubVerifier((_, _, _) => throw new IOException("Catalog unavailable"));
        var model = new ManagedFileVerificationViewModel(service, endOperation: () => ends++, dispatch: action => action());
        model.UpdateBooks([book]);
        await model.VerifyAllCommand.ExecuteAsync(null);
        Assert.Equal(1, ends);
        Assert.False(model.IsRunning);
        Assert.True(model.HasProblems);
        Assert.Contains("Catalog unavailable", model.StatusText);
        Assert.True(model.CanVerifyAll);
        model.ClearResults();
        Assert.False(model.HasProblems);

        var denied = new ManagedFileVerificationViewModel(service, () => false, () => ends++);
        denied.UpdateBooks([book]);
        await denied.VerifyAllCommand.ExecuteAsync(null);
        Assert.Single(service.Calls);
        Assert.Equal(1, ends);
        denied.IsAvailable = false;
        Assert.False(denied.VerifyAllCommand.CanExecute(null));
    }

    [Fact]
    public async Task DelayedUpdatesCannotOverwriteCompletedResultsOrANewerScan()
    {
        var book = Book("Book");
        var callbacks = new ConcurrentQueue<Action>();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var secondRun = false;
        var service = new StubVerifier((_, progress, _) =>
        {
            progress?.Report(new ManagedFileVerificationProgress(1, 1, book.Title, 5, 10));
            if (secondRun)
            {
                reached.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test gate was not released.");
            }
            return Report(Result(book, ManagedFileVerificationStatus.Unchanged));
        });
        var model = new ManagedFileVerificationViewModel(service, dispatch: callbacks.Enqueue);
        model.UpdateBooks([book]);
        await model.VerifyAllCommand.ExecuteAsync(null);
        var old = callbacks.ToArray();
        callbacks.Clear();
        Assert.NotEmpty(old);
        foreach (var callback in old) callback();
        Assert.Single(model.Results);
        Assert.Equal("Preparing read-only scan…", model.CurrentBookText);

        secondRun = true;
        var run = model.VerifyAllCommand.ExecuteAsync(null);
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            foreach (var callback in old) callback();
            Assert.Empty(model.Results);
            Assert.Equal("Preparing read-only scan…", model.CurrentBookText);
            release.Set();
            await run.WaitAsync(TimeSpan.FromSeconds(10));
            while (callbacks.TryDequeue(out var callback)) callback();
            Assert.Single(model.Results);
        }
        finally
        {
            release.Set();
            await run.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task RemovedBooksAndRestoredLibraries_DoNotKeepStaleAttentionMarkers()
    {
        var book = Book("Removed book");
        var model = new ManagedFileVerificationViewModel(new StubVerifier((_, _, _) =>
            Report(Result(book, ManagedFileVerificationStatus.Missing))), dispatch: action => action());
        model.UpdateBooks([book]);
        await model.VerifyAllCommand.ExecuteAsync(null);
        Assert.True(model.HasProblems);
        model.UpdateBooks([]);
        Assert.False(model.HasProblems);
        Assert.False(model.CanVerifyAll);
        model.ClearResults();
        Assert.False(model.HasResults);
        Assert.Contains("Not run yet", model.StatusText);
    }

    private static LibraryBook Book(string title) => new(Guid.NewGuid(), AudiobookMetadata.FromFileName(title),
        Path.Combine(Path.GetTempPath(), title + ".m4b"), 10, DateTimeOffset.UtcNow);
    private static ManagedFileVerificationResult Result(LibraryBook book, ManagedFileVerificationStatus status) =>
        new(book.Id, book.Title, book.FilePath, status, "Test finding", DateTimeOffset.UtcNow);
    private static ManagedFileVerificationReport Report(params ManagedFileVerificationResult[] results) => new(DateTimeOffset.UtcNow, results);

    private sealed class StubVerifier(Func<Guid?, IProgress<ManagedFileVerificationProgress>?, CancellationToken, ManagedFileVerificationReport> run) : IManagedFileVerifier
    {
        public List<Guid?> Calls { get; } = [];
        public ManagedFileVerificationReport Verify(Guid? bookId = null, IProgress<ManagedFileVerificationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(bookId);
            return run(bookId, progress, cancellationToken);
        }
    }
}
