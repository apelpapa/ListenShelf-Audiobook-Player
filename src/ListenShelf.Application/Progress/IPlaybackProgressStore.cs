namespace ListenShelf.Application.Progress;

public interface IPlaybackProgressStore
{
    PlaybackProgress? Get(string filePath);

    PlaybackProgress? GetMostRecent();

    void Save(PlaybackProgress progress);
}
