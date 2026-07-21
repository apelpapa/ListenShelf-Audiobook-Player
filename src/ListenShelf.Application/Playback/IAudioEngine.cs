namespace ListenShelf.Application.Playback;

public interface IAudioEngine : IDisposable
{
    event EventHandler<PlaybackProgressChangedEventArgs>? ProgressChanged;

    event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;

    event EventHandler<PlaybackChaptersChangedEventArgs>? ChaptersChanged;

    string? CurrentFilePath { get; }

    TimeSpan Position { get; }

    TimeSpan Duration { get; }

    int Volume { get; set; }

    double PlaybackRate { get; }

    IReadOnlyList<AudioChapter> Chapters { get; }

    int CurrentChapterIndex { get; }

    Task LoadAsync(string filePath, CancellationToken cancellationToken = default);

    bool Play();

    void Pause();

    void Stop();

    void Seek(TimeSpan position);

    bool TrySetPlaybackRate(double rate);

    bool TrySelectChapter(int chapterIndex);
}
