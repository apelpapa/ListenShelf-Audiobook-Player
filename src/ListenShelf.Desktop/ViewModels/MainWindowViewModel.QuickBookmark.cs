using CommunityToolkit.Mvvm.Input;
using ListenShelf.Application.Bookmarks;

namespace ListenShelf.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    [RelayCommand(CanExecute = nameof(CanCreateBookmark))]
    private void QuickBookmark()
    {
        if (!CanCreateBookmark || string.IsNullOrWhiteSpace(_currentFilePath))
        {
            return;
        }

        try
        {
            ErrorMessage = string.Empty;
            // A restored book may not have sent its saved position to the engine yet.
            var position = CurrentPlaybackPosition;
            var chapter = FindChapterContaining(position);
            var now = DateTimeOffset.UtcNow;
            _bookmarkStore.Save(new PlaybackBookmark(
                Guid.NewGuid(),
                _currentFilePath,
                position,
                Name: null,
                Note: null,
                chapter?.Index,
                chapter?.Title,
                now,
                now));
            RefreshBookmarks();
            ProgressText =
                $"Bookmark saved at {FormatTime(position.TotalSeconds, CurrentPlaybackDuration.TotalSeconds)} — use Edit to add a name or note.";
        }
        catch (Exception exception)
        {
            ErrorMessage = $"The bookmark could not be saved: {exception.Message}";
        }
    }
}
