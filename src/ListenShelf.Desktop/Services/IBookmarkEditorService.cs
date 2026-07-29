using ListenShelf.Application.Bookmarks;

namespace ListenShelf.Desktop.Services;

public interface IBookmarkEditorService
{
    Task<BookmarkEditResult?> EditAsync(PlaybackBookmark? bookmark);
}
