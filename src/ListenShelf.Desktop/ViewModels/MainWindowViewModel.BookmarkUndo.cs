using CommunityToolkit.Mvvm.Input;
using ListenShelf.Application.Bookmarks;

namespace ListenShelf.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    private PlaybackBookmark? _deletedBookmark;

    public bool HasDeletedBookmark => _deletedBookmark is not null;

    public bool CanUndoBookmarkDeletion => !_disposed && CanControlPlayback
        && _deletedBookmark is { } bookmark
        && !string.IsNullOrWhiteSpace(_currentFilePath)
        && PathsEqual(_currentFilePath, bookmark.FilePath);

    public string DeletedBookmarkText
    {
        get
        {
            if (_deletedBookmark is not { } bookmark) return string.Empty;
            var name = string.IsNullOrWhiteSpace(bookmark.Name)
                ? $"Bookmark at {FormatTime(bookmark.Position.TotalSeconds, bookmark.Position.TotalSeconds)}"
                : bookmark.Name;
            return $"Deleted: {name}";
        }
    }

    private void SetDeletedBookmark(PlaybackBookmark? bookmark)
    {
        _deletedBookmark = bookmark;
        OnPropertyChanged(nameof(HasDeletedBookmark));
        OnPropertyChanged(nameof(DeletedBookmarkText));
        OnPropertyChanged(nameof(CanUndoBookmarkDeletion));
        UndoBookmarkDeletionCommand.NotifyCanExecuteChanged();
    }

    private void DeleteBookmark(PlaybackBookmark bookmark)
    {
        if (_disposed || !CanControlPlayback || string.IsNullOrWhiteSpace(_currentFilePath)
            || !PathsEqual(_currentFilePath, bookmark.FilePath)) return;

        // Ignore repeated/stale clicks and capture the latest edited version, not an old item callback.
        var current = Bookmarks.FirstOrDefault(item => item.Bookmark.Id == bookmark.Id)?.Bookmark;
        if (current is null) return;

        try
        {
            ErrorMessage = string.Empty;
            _bookmarkStore.Delete(current.Id);
            // Replace the one-level undo only after deletion succeeds.
            SetDeletedBookmark(current);
            RefreshBookmarks();
            ProgressText = "Bookmark deleted.";
        }
        catch (Exception exception)
        {
            ErrorMessage = $"The bookmark could not be deleted: {exception.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanUndoBookmarkDeletion))]
    private void UndoBookmarkDeletion()
    {
        if (!CanUndoBookmarkDeletion || _deletedBookmark is not { } bookmark) return;

        try
        {
            ErrorMessage = string.Empty;
            // An existing copy may have been restored elsewhere. Never overwrite its newer edits.
            var alreadyPresent = _bookmarkStore.GetForFile(bookmark.FilePath).Any(item => item.Id == bookmark.Id);
            if (!alreadyPresent) _bookmarkStore.Save(bookmark);
            SetDeletedBookmark(null);
            RefreshBookmarks();
            ProgressText = alreadyPresent
                ? "The bookmark is already present. Its existing version was kept."
                : "Bookmark restored.";
        }
        catch (Exception exception)
        {
            // Retain the snapshot and Undo button so a temporary write failure can be retried.
            ErrorMessage = $"The bookmark could not be restored. You can try Undo again: {exception.Message}";
        }
    }

    [RelayCommand]
    private void DismissBookmarkUndo() => SetDeletedBookmark(null);
}
