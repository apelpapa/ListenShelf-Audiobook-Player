using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ListenShelf.Application.Library;

namespace ListenShelf.Desktop.ViewModels;

public sealed partial class ManagedFileRepairViewModel : ViewModelBase
{
    private readonly IManagedFileRepairer _repairer;
    private readonly Func<Task<string?>> _pickSource;
    private readonly Func<bool> _beginOperation;
    private readonly Action _endOperation;
    private readonly Action<VerificationBookOption> _preparePlayback;
    private readonly Func<ManagedFileRepairResult?, Task> _finishOperation;
    private readonly Action<Action> _dispatch;
    private CancellationTokenSource? _cancellation;
    private Task? _runningTask;
    private int _generation;

    public ManagedFileRepairViewModel(IManagedFileRepairer repairer, Func<Task<string?>> pickSource,
        Func<bool>? beginOperation = null, Action? endOperation = null,
        Action<VerificationBookOption>? preparePlayback = null,
        Func<ManagedFileRepairResult?, Task>? finishOperation = null, Action<Action>? dispatch = null)
    {
        _repairer = repairer;
        _pickSource = pickSource;
        _beginOperation = beginOperation ?? (() => true);
        _endOperation = endOperation ?? (() => { });
        _preparePlayback = preparePlayback ?? (_ => { });
        _finishOperation = finishOperation ?? (_ => Task.CompletedTask);
        _dispatch = dispatch ?? (action => Dispatcher.UIThread.Post(action));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChooseSource))]
    [NotifyPropertyChangedFor(nameof(CanRepair))]
    [NotifyCanExecuteChangedFor(nameof(ChooseSourceCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmRepairCommand))]
    private bool _isAvailable = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChooseSource))]
    [NotifyPropertyChangedFor(nameof(CanRepair))]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyCanExecuteChangedFor(nameof(ChooseSourceCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmRepairCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isCancellationRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isFinalizing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChooseSource))]
    [NotifyPropertyChangedFor(nameof(CanRepair))]
    [NotifyCanExecuteChangedFor(nameof(ChooseSourceCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmRepairCommand))]
    private VerificationBookOption? _selectedBook;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSource))]
    [NotifyPropertyChangedFor(nameof(CanRepair))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmRepairCommand))]
    private string? _sourcePath;

    [ObservableProperty] private string _statusText = "Choose a book, then select its original file or a known-good copy. A saved fingerprint is required.";
    [ObservableProperty] private string _progressText = string.Empty;
    [ObservableProperty] private double _percentage;

    public ObservableCollection<VerificationBookOption> Books { get; } = [];
    public bool CanChooseSource => IsAvailable && !IsRunning && SelectedBook is not null;
    public bool CanRepair => CanChooseSource && HasSource;
    public bool HasSource => SourcePath is not null;
    public bool CanCancel => IsRunning && !IsFinalizing && !IsCancellationRequested;

    partial void OnSelectedBookChanged(VerificationBookOption? value) => SourcePath = null;

    public void UpdateBooks(IReadOnlyList<LibraryBook> books)
    {
        var id = SelectedBook?.Id;
        var source = SourcePath;
        var path = SelectedBook?.FilePath;
        Books.Clear();
        foreach (var book in books.OrderBy(book => book.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(book => book.Id))
            Books.Add(new VerificationBookOption(book.Id, book.Title, book.FilePath));
        SelectedBook = Books.FirstOrDefault(book => book.Id == id);
        if (SelectedBook?.FilePath == path) SourcePath = source;
    }

    public void ClearSelection()
    {
        if (IsRunning) return;
        SelectedBook = null;
        SourcePath = null;
    }

    [RelayCommand(CanExecute = nameof(CanChooseSource))]
    private async Task ChooseSourceAsync()
    {
        if (!CanChooseSource || !_beginOperation()) return;
        var book = SelectedBook;
        try
        {
            var path = await _pickSource();
            if (SelectedBook == book && path is not null)
            {
                SourcePath = path;
                StatusText = "Review the selected book and source below, then confirm repair. Selecting a file alone changes nothing.";
            }
        }
        catch (Exception exception) { StatusText = $"Could not choose a repair file: {exception.Message}"; }
        finally { _endOperation(); }
    }

    [RelayCommand]
    private void DismissSource()
    {
        if (!IsRunning) SourcePath = null;
    }

    [RelayCommand(CanExecute = nameof(CanRepair))]
    private Task ConfirmRepairAsync()
    {
        if (!CanRepair || !_beginOperation()) return Task.CompletedTask;
        var book = SelectedBook!;
        var source = SourcePath!;
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        IsCancellationRequested = IsFinalizing = false;
        IsRunning = true;
        Percentage = 0;
        ProgressText = "Checking repair requirements…";
        StatusText = $"Repairing {book.Title}…";
        _runningTask = RunAsync(book, source, cancellation, ++_generation);
        return _runningTask;
    }

    private async Task RunAsync(VerificationBookOption book, string source, CancellationTokenSource cancellation, int generation)
    {
        ManagedFileRepairResult? result = null;
        try
        {
            _preparePlayback(book);
            var reporter = new RepairProgress(update => _dispatch(() =>
            {
                if (!IsRunning || generation != _generation) return;
                if (update.Stage == LibraryImportStage.Finalizing) IsFinalizing = true;
                if (IsCancellationRequested && !IsFinalizing) return;
                Percentage = update.StageFraction * 100;
                ProgressText = update.Detail ?? $"{update.Stage}: {update.ProcessedBytes / (1024d * 1024):0.##} of {update.TotalBytes / (1024d * 1024):0.##} MB";
            }));
            result = await Task.Run(() => _repairer.Repair(book.Id, source, reporter, cancellation.Token));
            StatusText = result.RetainedCopyPath is null
                ? $"Repaired {result.Title}. Its saved place, bookmarks, cover, and details are unchanged."
                : $"Repaired {result.Title}. Listening data and details are unchanged. The previous copy is retained below for optional confirmed cleanup.";
            SourcePath = null;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            StatusText = "Repair canceled before replacement. The source and managed audiobook were left unchanged.";
        }
        catch (Exception exception)
        {
            StatusText = $"Repair could not finish: {exception.Message}";
        }
        finally
        {
            // Keep the busy gate and close-wait active until playback and Storage
            // Care have been refreshed. Never restart listening automatically.
            try { await _finishOperation(result); }
            catch (Exception exception) { StatusText += $" Refresh needs attention: {exception.Message}"; }
            finally
            {
                IsRunning = false;
                cancellation.Dispose();
                _cancellation = null;
                _endOperation();
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        if (!CanCancel) return;
        IsCancellationRequested = true;
        StatusText = "Canceling repair… If final replacement has started, it will finish safely.";
        _cancellation?.Cancel();
    }

    public async Task CancelAndWaitAsync()
    {
        Cancel();
        if (_runningTask is { } task) await task;
    }

    private sealed class RepairProgress(Action<LibraryImportProgress> report) : IProgress<LibraryImportProgress>
    {
        private LibraryImportStage? _stage;
        private long _lastReport;
        public void Report(LibraryImportProgress value)
        {
            var now = Stopwatch.GetTimestamp();
            if (value.Stage != _stage || value.StageFraction >= 1 || Stopwatch.GetElapsedTime(_lastReport, now) >= TimeSpan.FromMilliseconds(100))
            {
                _stage = value.Stage;
                _lastReport = now;
                report(value);
            }
        }
    }
}
