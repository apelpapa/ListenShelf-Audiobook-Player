namespace ListenShelf.Application.Playback;

public interface IAudioEngine : IDisposable
{
    event EventHandler<PlaybackProgressChangedEventArgs>? ProgressChanged;

    event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;

    string? CurrentFilePath { get; }

    TimeSpan Position { get; }

    TimeSpan Duration { get; }

    int Volume { get; set; }

    double PlaybackRate { get; }

    Task LoadAsync(string filePath, CancellationToken cancellationToken = default);

    bool Play();

    void Pause();

    void Stop();

    void Seek(TimeSpan position);

    bool TrySetPlaybackRate(double rate);
}
