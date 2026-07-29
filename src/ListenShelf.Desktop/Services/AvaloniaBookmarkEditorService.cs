using Avalonia.Controls;
using ListenShelf.Application.Bookmarks;
using ListenShelf.Desktop.ViewModels;
using ListenShelf.Desktop.Views;

namespace ListenShelf.Desktop.Services;

public sealed class AvaloniaBookmarkEditorService(Window owner) : IBookmarkEditorService
{
    public Task<BookmarkEditResult?> EditAsync(PlaybackBookmark? bookmark)
    {
        var dialog = new BookmarkEditorWindow
        {
            DataContext = new BookmarkEditorViewModel(bookmark),
        };

        return dialog.ShowDialog<BookmarkEditResult?>(owner);
    }
}
