using CommunityToolkit.Mvvm.Input;

namespace ListenShelf.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    // Capture a fixed media boundary, not a wall-clock deadline or whichever
    // chapter the engine happens to select later. This state is session-only.
    private ChapterSleepTarget? _chapterSleepTarget;

    public bool IsChapterSleepActive => _chapterSleepTarget is not null;
    public bool CanAddTenMinutesToSleepTimer => IsSleepTimerActive && !IsChapterSleepActive;
    public bool CanStopAtChapterEnd => !_disposed && CanControlPlayback
        && GetCurrentChapterEnd() is { } chapter && chapter.EndSeconds > PositionSeconds;

    [RelayCommand(CanExecute = nameof(CanStopAtChapterEnd))]
    private void StartChapterSleepTimer()
    {
        if (!CanStopAtChapterEnd || GetCurrentChapterEnd() is not { } chapter) return;
        StopSleepTimer();
        _sleepTimerPausePending = false;
        SetChapterSleepTarget(new ChapterSleepTarget(chapter.Number, chapter.EndSeconds));
        IsSleepTimerActive = true;
        _sleepTimer.Start();
    }

    private void EvaluateChapterSleep(double positionSeconds)
    {
        if (_disposed || !CanControlPlayback || !double.IsFinite(positionSeconds)
            || _chapterSleepTarget is not { } target || positionSeconds < target.EndSeconds) return;
        FinishSleepTimer();
    }

    private void SetChapterSleepTarget(ChapterSleepTarget? target)
    {
        _chapterSleepTarget = target;
        OnPropertyChanged(nameof(IsChapterSleepActive));
        OnPropertyChanged(nameof(CanAddTenMinutesToSleepTimer));
        OnPropertyChanged(nameof(SleepTimerButtonText));
        OnPropertyChanged(nameof(SleepTimerStatusText));
        AddTenMinutesToSleepTimerCommand.NotifyCanExecuteChanged();
    }

    private void NotifyChapterSleepAvailability()
    {
        OnPropertyChanged(nameof(CanStopAtChapterEnd));
        StartChapterSleepTimerCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsFileLoadedChanged(bool value)
    {
        UpdateLibraryPlaybackIndicators();
        // Loading another book (including repair/restore) cannot carry a media
        // position from the old book into the new one.
        if (!value && IsChapterSleepActive)
        {
            _sleepTimerPausePending = false;
            StopSleepTimer();
        }
    }

    private sealed record ChapterSleepTarget(int Number, double EndSeconds);
}
