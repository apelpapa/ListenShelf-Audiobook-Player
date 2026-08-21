using CommunityToolkit.Mvvm.Input;

namespace ListenShelf.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private bool _isMuted;
    private double _lastAudibleVolume = DefaultPlaybackVolume;

    public bool IsMuted => _isMuted || Math.Round(Volume) <= 0;
    public bool IsVolumeAudible => !IsMuted;
    public bool CanToggleMute => !_disposed;
    public string MuteButtonToolTip => IsMuted ? "Unmute (M)" : "Mute (M)";

    [RelayCommand(CanExecute = nameof(CanToggleMute))]
    private void ToggleMute()
    {
        if (!CanToggleMute) return;

        if (IsMuted)
        {
            _isMuted = false;
            if (Math.Round(Volume) <= 0)
            {
                // The slider may have been taken all the way to zero. Restore
                // its last audible level, or the default for a fresh session.
                Volume = _lastAudibleVolume;
                return;
            }
            _audioEngine.Volume = (int)Math.Round(Volume);
        }
        else
        {
            _isMuted = true;
            _audioEngine.Volume = 0;
        }

        NotifyMuteState();
    }

    private void NotifyMuteState()
    {
        OnPropertyChanged(nameof(IsMuted));
        OnPropertyChanged(nameof(IsVolumeAudible));
        OnPropertyChanged(nameof(MuteButtonToolTip));
    }
}
