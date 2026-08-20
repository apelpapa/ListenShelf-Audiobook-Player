using CommunityToolkit.Mvvm.Input;

namespace ListenShelf.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private long _playbackSessionVersion;

    public bool CanJumpToTime => !_disposed && CanControlPlayback
        && double.IsFinite(DurationSeconds) && DurationSeconds > 0
        && DurationSeconds < TimeSpan.MaxValue.TotalSeconds;

    // A dialog result belongs to the book session that opened it, even if the
    // same file is unloaded and reopened while the dialog is outstanding.
    partial void OnIsFileLoadedChanging(bool value) => _playbackSessionVersion++;

    [RelayCommand(CanExecute = nameof(CanJumpToTime))]
    private async Task JumpToTimeAsync()
    {
        if (!CanJumpToTime) return;
        var session = _playbackSessionVersion;
        var filePath = _currentFilePath;

        try
        {
            var result = await _jumpToTimeService.ShowAsync(CurrentPlaybackPosition, CurrentPlaybackDuration);
            if (result is not { } position || !CanJumpToTime
                || session != _playbackSessionVersion || filePath != _currentFilePath) return;
            if (position < TimeSpan.Zero || position > CurrentPlaybackDuration)
            {
                ErrorMessage = "That time is outside the audiobook. Open Jump to time and try again.";
                return;
            }

            ErrorMessage = string.Empty;
            // Use the existing seek/save/resume path. Never call Play or Pause;
            // an armed chapter sleep timer still obeys its chosen endpoint.
            SeekPlayback(position);
            _isUpdatingPositionFromEngine = true;
            try
            {
                PositionSeconds = position.TotalSeconds;
            }
            finally
            {
                _isUpdatingPositionFromEngine = false;
            }
            SelectChapterContaining(position);
        }
        catch (Exception exception)
        {
            if (!_disposed) ErrorMessage = $"Could not jump to that time: {exception.Message}";
        }
    }
}
