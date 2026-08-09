using CommunityToolkit.Mvvm.ComponentModel;
using ListenShelf.Application.Settings;

namespace ListenShelf.Desktop.ViewModels;

public partial class PlaybackSkipSettingsViewModel : ViewModelBase
{
    private readonly IAppSettingsStore _store;
    private bool _isReloading;

    public PlaybackSkipSettingsViewModel(IAppSettingsStore store)
    {
        _store = store;
        Reload();
    }

    public int MinimumSeconds => PlaybackSkipIntervals.MinimumSeconds;

    public int MaximumSeconds => PlaybackSkipIntervals.MaximumSeconds;

    [ObservableProperty]
    private decimal? _rewindSeconds;

    [ObservableProperty]
    private decimal? _forwardSeconds;

    [ObservableProperty]
    private string _rewindMessage = string.Empty;

    [ObservableProperty]
    private string _forwardMessage = string.Empty;

    public int EffectiveRewindSeconds { get; private set; } = PlaybackSkipIntervals.DefaultRewindSeconds;

    public int EffectiveForwardSeconds { get; private set; } = PlaybackSkipIntervals.DefaultForwardSeconds;

    public string RewindButtonText => $"−{EffectiveRewindSeconds} sec";

    public string ForwardButtonText => $"+{EffectiveForwardSeconds} sec";

    public string RewindToolTip =>
        $"Rewind {EffectiveRewindSeconds} seconds (Left Arrow, J, or Previous media button)";

    public string ForwardToolTip =>
        $"Forward {EffectiveForwardSeconds} seconds (Right Arrow, L, or Next media button)";

    public void Reload()
    {
        _isReloading = true;
        try
        {
            var rewind = LoadInterval(_store.GetRewindSeconds, PlaybackSkipIntervals.DefaultRewindSeconds);
            var forward = LoadInterval(_store.GetForwardSeconds, PlaybackSkipIntervals.DefaultForwardSeconds);
            RewindSeconds = rewind.Seconds;
            ForwardSeconds = forward.Seconds;
            SetEffectiveInterval(rewind.Seconds, isRewind: true);
            SetEffectiveInterval(forward.Seconds, isRewind: false);
            RewindMessage = rewind.Message;
            ForwardMessage = forward.Message;
        }
        finally
        {
            _isReloading = false;
        }
    }

    partial void OnRewindSecondsChanged(decimal? value)
    {
        if (!_isReloading)
        {
            RewindMessage = ApplyInterval(value, isRewind: true);
        }
    }

    partial void OnForwardSecondsChanged(decimal? value)
    {
        if (!_isReloading)
        {
            ForwardMessage = ApplyInterval(value, isRewind: false);
        }
    }

    private string ApplyInterval(decimal? value, bool isRewind)
    {
        if (value is not { } seconds
            || seconds != decimal.Truncate(seconds)
            || seconds < MinimumSeconds
            || seconds > MaximumSeconds)
        {
            return $"Enter a whole number from {MinimumSeconds}–{MaximumSeconds}. The previous interval is still active.";
        }

        var interval = decimal.ToInt32(seconds);
        SetEffectiveInterval(interval, isRewind);
        try
        {
            if (isRewind)
            {
                _store.SaveRewindSeconds(interval);
            }
            else
            {
                _store.SaveForwardSeconds(interval);
            }

            return "Saved for all audiobooks.";
        }
        catch (Exception exception)
        {
            return $"Active for this session, but could not be saved: {exception.Message}";
        }
    }

    private void SetEffectiveInterval(int seconds, bool isRewind)
    {
        if (isRewind)
        {
            EffectiveRewindSeconds = seconds;
            OnPropertyChanged(nameof(EffectiveRewindSeconds));
            OnPropertyChanged(nameof(RewindButtonText));
            OnPropertyChanged(nameof(RewindToolTip));
        }
        else
        {
            EffectiveForwardSeconds = seconds;
            OnPropertyChanged(nameof(EffectiveForwardSeconds));
            OnPropertyChanged(nameof(ForwardButtonText));
            OnPropertyChanged(nameof(ForwardToolTip));
        }
    }

    private static (int Seconds, string Message) LoadInterval(Func<int> load, int defaultValue)
    {
        try
        {
            var seconds = load();
            return (PlaybackSkipIntervals.IsValid(seconds) ? seconds : defaultValue, string.Empty);
        }
        catch (Exception exception)
        {
            return (defaultValue, $"Using the default; the saved interval could not be loaded: {exception.Message}");
        }
    }
}
