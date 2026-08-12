using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ListenShelf.Application.Library;

namespace ListenShelf.Desktop.ViewModels;

public sealed partial class LibraryImportViewModel : ViewModelBase
{
    private readonly LibraryImportBatch _batch;
    private readonly Action<Action> _dispatch;
    private CancellationTokenSource? _cancellation;
    private Task<LibraryImportBatchResult>? _runningTask;
    private int _runId;

    public LibraryImportViewModel(IAudiobookLibrary library, Action<Action>? dispatch = null)
    {
        _batch = new LibraryImportBatch(library);
        _dispatch = dispatch ?? (action => Dispatcher.UIThread.Post(action));
    }

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyPropertyChangedFor(nameof(CanDismiss))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(DismissCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isCancellationRequested;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _currentFileName = string.Empty;

    [ObservableProperty]
    private string _bookNumberText = string.Empty;

    [ObservableProperty]
    private string _stageText = string.Empty;

    [ObservableProperty]
    private string _byteProgressText = string.Empty;

    [ObservableProperty]
    private double _stagePercentage;

    [ObservableProperty]
    private double _overallPercentage;

    [ObservableProperty]
    private bool _isStageIndeterminate;

    [ObservableProperty]
    private bool _areDetailsExpanded;

    public ObservableCollection<LibraryImportItemViewModel> Results { get; } = [];
    public bool HasResults => Results.Count > 0;
    public bool CanCancel => IsRunning && !IsCancellationRequested;
    public bool CanDismiss => !IsRunning;

    public Task<LibraryImportBatchResult> ImportAsync(IReadOnlyList<string> filePaths)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("An import is already running.");
        }

        var paths = filePaths.ToArray();
        var runId = ++_runId;
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        Results.Clear();
        OnPropertyChanged(nameof(HasResults));
        AreDetailsExpanded = false;
        IsVisible = true;
        IsRunning = true;
        IsCancellationRequested = false;
        StatusText = "Importing audiobooks. Originals stay untouched.";
        CurrentFileName = string.Empty;
        BookNumberText = $"0 of {paths.Length} books processed";
        StageText = "Preparing…";
        ByteProgressText = string.Empty;
        StagePercentage = 0;
        OverallPercentage = 0;
        IsStageIndeterminate = true;
        _runningTask = RunAsync(paths, cancellation, runId);
        return _runningTask;
    }

    public async Task CancelAndWaitAsync()
    {
        Cancel();
        if (_runningTask is { } runningTask)
        {
            await runningTask;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        if (!CanCancel)
        {
            return;
        }

        IsCancellationRequested = true;
        StatusText = "Canceling safely… Completed imports will be kept. Please wait for cleanup.";
        _cancellation?.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanDismiss))]
    private void Dismiss()
    {
        if (CanDismiss)
        {
            IsVisible = false;
        }
    }

    private async Task<LibraryImportBatchResult> RunAsync(
        string[] paths, CancellationTokenSource cancellation, int runId)
    {
        try
        {
            var reporter = new ThrottledProgress(update => _dispatch(() =>
            {
                // Ignore delayed updates after cancellation, completion, or a new batch.
                if (runId == _runId && IsRunning && !IsCancellationRequested)
                {
                    ApplyProgress(update);
                }
            }));
            var result = await Task.Run(() => _batch.Run(paths, reporter, cancellation.Token));
            Results.Clear();
            foreach (var file in result.Files)
            {
                Results.Add(new LibraryImportItemViewModel(file));
            }

            OnPropertyChanged(nameof(HasResults));
            StatusText = result.Summary;
            AreDetailsExpanded = result.FailedCount > 0 || result.WasCanceled;
            return result;
        }
        catch (Exception exception)
        {
            StatusText = $"Import stopped unexpectedly: {exception.Message}";
            AreDetailsExpanded = true;
            throw;
        }
        finally
        {
            IsRunning = false;
            cancellation.Dispose();
            _cancellation = null;
        }
    }

    private void ApplyProgress(LibraryImportBatchProgress update)
    {
        CurrentFileName = Path.GetFileName(update.FilePath);
        BookNumberText = $"Book {update.FileNumber} of {update.TotalFiles}";
        OverallPercentage = update.OverallPercentage;
        StagePercentage = update.FileProgress.StageFraction * 100d;
        StageText = update.FileProgress.Stage switch
        {
            LibraryImportStage.Copying => "Copying",
            LibraryImportStage.Verifying => "Verifying SHA-256",
            LibraryImportStage.Finalizing => "Saving to library",
            _ => "Checking file and duplicates",
        };
        IsStageIndeterminate = update.FileProgress.Stage is LibraryImportStage.Checking or LibraryImportStage.Finalizing;
        ByteProgressText = IsStageIndeterminate ? string.Empty
            : $"{FormatBytes(update.FileProgress.ProcessedBytes)} of {FormatBytes(update.FileProgress.TotalBytes)} · {StagePercentage:0}%";
        if (update.CompletedFile is { } completed)
        {
            Results.Add(new LibraryImportItemViewModel(completed));
            OnPropertyChanged(nameof(HasResults));
        }
    }

    private static string FormatBytes(long bytes) => bytes >= 1024L * 1024 * 1024
        ? $"{bytes / (1024d * 1024 * 1024):0.##} GB"
        : bytes >= 1024 * 1024 ? $"{bytes / (1024d * 1024):0.##} MB"
        : bytes >= 1024 ? $"{bytes / 1024d:0.##} KB" : $"{bytes} B";

    private sealed class ThrottledProgress(Action<LibraryImportBatchProgress> report)
        : IProgress<LibraryImportBatchProgress>
    {
        private long _lastReport;
        private int _fileNumber;
        private LibraryImportStage? _stage;

        public void Report(LibraryImportBatchProgress value)
        {
            var now = Stopwatch.GetTimestamp();
            if (value.CompletedFile is not null || value.FileNumber != _fileNumber
                || value.FileProgress.Stage != _stage || value.FileProgress.StageFraction >= 1
                || Stopwatch.GetElapsedTime(_lastReport, now) >= TimeSpan.FromMilliseconds(100))
            {
                _lastReport = now;
                _fileNumber = value.FileNumber;
                _stage = value.FileProgress.Stage;
                report(value);
            }
        }
    }
}

public sealed class LibraryImportItemViewModel(LibraryImportFileResult result)
{
    public string FileName => Path.GetFileName(result.FilePath);
    public string FilePath => result.FilePath;
    public string Detail => result.Message;
    public string StatusText => result.Outcome switch
    {
        LibraryImportOutcome.Added => "Added",
        LibraryImportOutcome.AlreadyInLibrary => "Already in library",
        LibraryImportOutcome.Failed => "Failed",
        LibraryImportOutcome.Canceled => "Canceled",
        _ => "Not processed",
    };
}
