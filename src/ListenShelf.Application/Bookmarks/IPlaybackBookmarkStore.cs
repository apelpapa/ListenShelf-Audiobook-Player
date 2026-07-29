namespace ListenShelf.Application.Bookmarks;

public interface IPlaybackBookmarkStore
{
    IReadOnlyList<PlaybackBookmark> GetForFile(string filePath);

    void Save(PlaybackBookmark bookmark);

    void Delete(Guid bookmarkId);
}
