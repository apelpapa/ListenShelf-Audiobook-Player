using ListenShelf.Application.Playback;

namespace ListenShelf.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private PlaybackState _libraryPlaybackState = PlaybackState.Ready;

    partial void OnIsPlayingChanged(bool value)
    {
        if (value) _libraryPlaybackState = PlaybackState.Playing;
        else if (_libraryPlaybackState == PlaybackState.Playing) _libraryPlaybackState = PlaybackState.Paused;
        UpdateLibraryPlaybackIndicators();
    }

    private void UpdateLibraryPlaybackIndicators()
    {
        foreach (var book in LibraryBooks)
        {
            var isCurrentBook = !_disposed && IsFileLoaded
                && !string.IsNullOrWhiteSpace(_currentFilePath)
                && PathsEqual(book.FilePath, _currentFilePath);
            book.UpdatePlaybackIndicator(isCurrentBook, _libraryPlaybackState);
        }
    }
}
