using CommunityToolkit.Mvvm.Input;
using ListenShelf.Application.Settings;

namespace ListenShelf.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private int? _lastSleepTimerMinutes;

    public int? LastSleepTimerMinutes => _lastSleepTimerMinutes;
    public bool CanStartLastSleepTimer => !_disposed && CanControlPlayback && LastSleepTimerMinutes.HasValue;
    public string LastSleepTimerMenuText => LastSleepTimerMinutes is { } minutes
        ? $"Start last timer ({minutes} minute{(minutes == 1 ? string.Empty : "s")})"
        : "Start last timer";
    public string LastSleepTimerToolTip => LastSleepTimerMinutes.HasValue
        ? "Start a fresh countdown using the last preset or custom duration, replacing any active timer."
        : "Choose a preset or custom duration first. No timer starts automatically.";

    [RelayCommand(CanExecute = nameof(CanStartLastSleepTimer))]
    private void StartLastSleepTimer()
    {
        if (CanStartLastSleepTimer && LastSleepTimerMinutes is { } minutes)
            StartSleepTimer(minutes);
    }

    private void SetLastSleepTimerMinutes(int? minutes)
    {
        _lastSleepTimerMinutes = minutes is { } value && SleepTimerDurations.IsValid(value) ? value : null;
        OnPropertyChanged(nameof(LastSleepTimerMinutes));
        OnPropertyChanged(nameof(LastSleepTimerMenuText));
        OnPropertyChanged(nameof(LastSleepTimerToolTip));
        OnPropertyChanged(nameof(CanStartLastSleepTimer));
        StartLastSleepTimerCommand.NotifyCanExecuteChanged();
    }

    private void LoadLastSleepTimerDuration()
    {
        // Loading a preference never arms a timer or writes a default back.
        SetLastSleepTimerMinutes(null);
        try
        {
            SetLastSleepTimerMinutes(_appSettingsStore.GetLastSleepTimerMinutes());
        }
        catch (Exception exception)
        {
            ErrorMessage = $"The last sleep-timer duration could not be loaded: {exception.Message}";
        }
    }

    private void RememberSleepTimerDuration(int minutes)
    {
        SetLastSleepTimerMinutes(minutes);
        try
        {
            _appSettingsStore.SaveLastSleepTimerMinutes(minutes);
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Sleep timer started, but its duration could not be remembered for next time: {exception.Message}";
        }
    }
}
