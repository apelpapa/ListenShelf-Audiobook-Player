using CommunityToolkit.Mvvm.Input;

namespace ListenShelf.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    public bool CanStartCustomSleepTimer => !_disposed && CanControlPlayback;

    [RelayCommand(CanExecute = nameof(CanStartCustomSleepTimer))]
    private async Task StartCustomSleepTimerAsync()
    {
        if (!CanStartCustomSleepTimer) return;
        var session = _playbackSessionVersion;

        try
        {
            // Do not cancel or alter an existing timer while the user is editing.
            var result = await _sleepTimerDurationService.ChooseMinutesAsync();
            if (result is not { } minutes || !CanStartCustomSleepTimer
                || session != _playbackSessionVersion) return;

            if (!CustomSleepTimerViewModel.IsValidDuration(minutes))
            {
                ErrorMessage = $"Sleep timer duration must be {CustomSleepTimerViewModel.MinimumMinutes}–{CustomSleepTimerViewModel.MaximumMinutes} whole minutes.";
                return;
            }

            ErrorMessage = string.Empty;
            StartSleepTimer(TimeSpan.FromMinutes(minutes));
        }
        catch (Exception exception)
        {
            if (!_disposed) ErrorMessage = $"The sleep timer could not be set: {exception.Message}";
        }
    }
}
