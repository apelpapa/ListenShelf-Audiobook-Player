namespace ListenShelf.Application.Progress;

public interface IPlaybackProgressStore
{
    PlaybackProgress? Get(string filePath);

    void Save(PlaybackProgress progress);
}
