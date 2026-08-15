using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ListenShelf.Application.Library;

namespace ListenShelf.Desktop.ViewModels;

public sealed partial class ManagedFileVerificationViewModel : ViewModelBase
{
    private readonly IManagedFileVerifier _verifier;
    private readonly Func<bool> _beginOperation;
    private readonly Action _endOperation;
    private readonly Action<Action> _dispatch;
    private readonly Dictionary<Guid, ManagedFileVerificationResult> _lastChecks = [];
    private CancellationTokenSource? _cancellation;
    private Task? _runningTask;
    private int _generation;
    private bool _scanFailed;

    public ManagedFileVerificationViewModel(IManagedFileVerifier verifier, Func<bool>? beginOperation = null,
        Action? endOperation = null, Action<Action>? dispatch = null)
    {
        _verifier = verifier;
        _beginOperation = beginOperation ?? (() => true);
        _endOperation = endOperation ?? (() => { });
        _dispatch = dispatch ?? (action => Dispatcher.UIThread.Post(action));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanVerifyAll))]
    [NotifyPropertyChangedFor(nameof(CanVerifySelected))]
    [NotifyCanExecuteChangedFor(nameof(VerifyAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(VerifySelectedCommand))]
    private bool _isAvailable = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanVerifyAll))]
    [NotifyPropertyChangedFor(nameof(CanVerifySelected))]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyCanExecuteChangedFor(nameof(VerifyAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(VerifySelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isCancellationRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanVerifySelected))]
    [NotifyCanExecuteChangedFor(nameof(VerifySelectedCommand))]
    private VerificationBookOption? _selectedBook;

    [ObservableProperty] private string _statusText = "Not run yet. Choose a book or verify the entire library.";
    [ObservableProperty] private string _lastCheckedText = "Checks run only when requested. Files and saved fingerprints are never changed.";
    [ObservableProperty] private string _currentBookText = string.Empty;
    [ObservableProperty] private string _byteProgressText = string.Empty;
    [ObservableProperty] private double _filePercentage;
    [ObservableProperty] private double _overallPercentage;

    public ObservableCollection<VerificationBookOption> Books { get; } = [];
    public ObservableCollection<ManagedFileVerificationResult> Results { get; } = [];
    public bool HasResults => Results.Count > 0;
    public bool HasProblems => _scanFailed || _lastChecks.Values.Any(result => result.NeedsAttention);
    public IReadOnlyList<ManagedFileVerificationResult> KnownProblems => _lastChecks.Values
        .Where(result => result.NeedsAttention).OrderBy(result => result.Title).ToArray();
    public bool CanVerifyAll => IsAvailable && !IsRunning && Books.Count > 0;
    public bool CanVerifySelected => CanVerifyAll && SelectedBook is not null;
    public bool CanCancel => IsRunning && !IsCancellationRequested;

    public void UpdateBooks(IReadOnlyList<LibraryBook> books)
    {
        var selectedId = SelectedBook?.Id;
        Books.Clear();
        foreach (var book in books.OrderBy(book => book.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(book => book.Id))
        {
            Books.Add(new VerificationBookOption(book.Id, book.Title, book.FilePath));
        }

        SelectedBook = Books.FirstOrDefault(book => book.Id == selectedId);
        var ids = books.Select(book => book.Id).ToHashSet();
        foreach (var id in _lastChecks.Keys.Where(id => !ids.Contains(id)).ToArray())
        {
            _lastChecks.Remove(id);
        }

        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(KnownProblems));
        OnPropertyChanged(nameof(CanVerifyAll));
        OnPropertyChanged(nameof(CanVerifySelected));
        VerifyAllCommand.NotifyCanExecuteChanged();
        VerifySelectedCommand.NotifyCanExecuteChanged();
    }

    // A confirmed repair resolves only the matching book's previous finding.
    public void RecordRepair(ManagedFileRepairResult repair)
    {
        if (IsRunning) return;
        var result = new ManagedFileVerificationResult(repair.BookId, repair.Title, repair.FilePath,
            ManagedFileVerificationStatus.Unchanged, "A replacement matching the saved fingerprint was verified and installed by a confirmed repair.", DateTimeOffset.UtcNow);
        RememberResult(result);
        for (var index = 0; index < Results.Count; index++)
        {
            if (Results[index].BookId == repair.BookId) Results[index] = result;
        }

        LastCheckedText = $"Repair completed for {repair.Title} at {result.CheckedAtUtc.ToLocalTime():g}. Other results retain their original scan times.";
        StatusText = $"Confirmed repair updated the finding for {repair.Title}. Verify again whenever you want a fresh check.";
    }

    // A full backup restore replaces the files to which old session results refer.
    public void ClearResults()
    {
        if (IsRunning) return;
        _lastChecks.Clear();
        Results.Clear();
        _scanFailed = false;
        StatusText = "Not run yet. Choose a book or verify the entire library.";
        LastCheckedText = "Checks run only when requested. Files and saved fingerprints are never changed.";
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(KnownProblems));
    }

    [RelayCommand(CanExecute = nameof(CanVerifyAll))]
    private Task VerifyAllAsync() => StartAsync(null);

    [RelayCommand(CanExecute = nameof(CanVerifySelected))]
    private Task VerifySelectedAsync() => CanVerifySelected ? StartAsync(SelectedBook!.Id) : Task.CompletedTask;

    private Task StartAsync(Guid? bookId)
    {
        if (!CanVerifyAll || !_beginOperation()) return Task.CompletedTask;
        var generation = ++_generation;
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        IsCancellationRequested = false;
        IsRunning = true;
        Results.Clear();
        _scanFailed = false;
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasProblems));
        StatusText = "Verifying managed audiobook files… Playback can continue.";
        CurrentBookText = "Preparing read-only scan…";
        ByteProgressText = string.Empty;
        FilePercentage = OverallPercentage = 0;
        var scope = bookId is null ? "Entire library" : SelectedBook?.Title ?? "Selected book";
        _runningTask = RunAsync(bookId, scope, cancellation, generation);
        return _runningTask;
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        if (!CanCancel) return;
        IsCancellationRequested = true;
        StatusText = "Canceling verification… Completed results will be kept.";
        _cancellation?.Cancel();
    }

    public async Task CancelAndWaitAsync()
    {
        Cancel();
        if (_runningTask is { } task) await task;
    }

    private async Task RunAsync(Guid? bookId, string scope, CancellationTokenSource cancellation, int generation)
    {
        try
        {
            var reporter = new VerificationProgress(update => _dispatch(() =>
            {
                if (generation != _generation || !IsRunning || IsCancellationRequested) return;
                CurrentBookText = $"Book {update.BookNumber} of {update.TotalBooks} — {update.Title}";
                FilePercentage = update.FilePercentage;
                OverallPercentage = update.OverallPercentage;
                ByteProgressText = $"{update.ProcessedBytes / (1024d * 1024):0.##} of {update.TotalBytes / (1024d * 1024):0.##} MB · {FilePercentage:0}%";
                if (update.CompletedBook is { } completed)
                {
                    Results.Add(completed);
                    RememberResult(completed);
                    OnPropertyChanged(nameof(HasResults));
                }
            }));
            var report = await Task.Run(() => _verifier.Verify(bookId, reporter, cancellation.Token));
            Results.Clear();
            foreach (var result in report.Results)
            {
                Results.Add(result);
                RememberResult(result);
            }

            StatusText = report.Summary;
            LastCheckedText = $"{scope} · {report.FinishedAtUtc.ToLocalTime():g}. Results describe that scan only; later changes require another check.";
            OnPropertyChanged(nameof(HasResults));
        }
        catch (Exception exception)
        {
            _scanFailed = true;
            StatusText = $"Verification could not finish: {exception.Message}";
            LastCheckedText = "The latest scan did not finish. No files or fingerprints were changed.";
        }
        finally
        {
            IsRunning = false;
            cancellation.Dispose();
            _cancellation = null;
            OnPropertyChanged(nameof(HasProblems));
            _endOperation();
        }
    }

    private void RememberResult(ManagedFileVerificationResult result)
    {
        // A canceled check must not erase a previously discovered problem.
        if (result.WasChecked) _lastChecks[result.BookId] = result;
        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(KnownProblems));
    }

    private sealed class VerificationProgress(Action<ManagedFileVerificationProgress> report) : IProgress<ManagedFileVerificationProgress>
    {
        private int _bookNumber;
        private long _lastReport;
        public void Report(ManagedFileVerificationProgress value)
        {
            var now = Stopwatch.GetTimestamp();
            if (value.CompletedBook is not null || value.BookNumber != _bookNumber || value.FilePercentage >= 100
                || Stopwatch.GetElapsedTime(_lastReport, now) >= TimeSpan.FromMilliseconds(100))
            {
                _bookNumber = value.BookNumber;
                _lastReport = now;
                report(value);
            }
        }
    }
}

public sealed record VerificationBookOption(Guid Id, string Title, string FilePath)
{
    public string DisplayName => $"{Title} — {Path.GetFileName(FilePath)}";
}
