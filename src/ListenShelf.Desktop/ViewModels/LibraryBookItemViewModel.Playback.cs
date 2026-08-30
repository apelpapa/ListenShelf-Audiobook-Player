using ListenShelf.Application.Playback;

namespace ListenShelf.Desktop.ViewModels;

public sealed partial class LibraryBookItemViewModel
{
    private bool _isCurrentBook;
    private PlaybackState _currentPlaybackState = PlaybackState.Ready;

    public bool IsCurrentBook => _isCurrentBook;

    public bool IsCurrentBookPlaying => IsCurrentBook && _currentPlaybackState == PlaybackState.Playing;

    public string PlaybackIndicatorText => !IsCurrentBook ? string.Empty : _currentPlaybackState switch
    {
        PlaybackState.Playing => "Current · Playing",
        PlaybackState.Paused => "Current · Paused",
        PlaybackState.Ended => "Current · Finished",
        PlaybackState.Error => "Current · Error",
        PlaybackState.Loading => "Current · Loading",
        PlaybackState.Stopped => "Current · Stopped",
        _ => "Current · Ready",
    };

    public string PlaybackIndicatorToolTip => IsCurrentBook
        ? $"{Title} is loaded in the Player. {PlaybackIndicatorText}."
        : string.Empty;

    public void UpdatePlaybackIndicator(bool isCurrentBook, PlaybackState state)
    {
        if (!isCurrentBook) state = PlaybackState.Ready;
        if (_isCurrentBook == isCurrentBook && _currentPlaybackState == state) return;
        _isCurrentBook = isCurrentBook;
        _currentPlaybackState = state;
        OnPropertyChanged(nameof(IsCurrentBook));
        OnPropertyChanged(nameof(IsCurrentBookPlaying));
        OnPropertyChanged(nameof(PlaybackIndicatorText));
        OnPropertyChanged(nameof(PlaybackIndicatorToolTip));
    }
}
