using LibVLCSharp.Shared;
using ListenShelf.Application.Playback;

namespace ListenShelf.Playback.LibVlc;

public sealed class LibVlcAudioEngine : IAudioEngine
{
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _mediaPlayer;
    private Media? _currentMedia;
    private double _requestedPlaybackRate = 1d;
    private bool _hasReachedEnd;
    private bool _disposed;

    public LibVlcAudioEngine()
    {
        Core.Initialize();

        _libVlc = new LibVLC("--no-video", "--no-video-title-show");
        _mediaPlayer = new MediaPlayer(_libVlc);

        _mediaPlayer.Opening += OnOpening;
        _mediaPlayer.Playing += OnPlaying;
        _mediaPlayer.Paused += OnPaused;
        _mediaPlayer.Stopped += OnStopped;
        _mediaPlayer.EndReached += OnEndReached;
        _mediaPlayer.EncounteredError += OnEncounteredError;
        _mediaPlayer.TimeChanged += OnTimeChanged;
        _mediaPlayer.LengthChanged += OnLengthChanged;

        Volume = 80;
    }

    public event EventHandler<PlaybackProgressChangedEventArgs>? ProgressChanged;

    public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;

    public string? CurrentFilePath { get; private set; }

    public TimeSpan Position => TimeSpan.FromMilliseconds(Math.Max(0, _mediaPlayer.Time));

    public TimeSpan Duration => TimeSpan.FromMilliseconds(Math.Max(0, _mediaPlayer.Length));

    public int Volume
    {
        get => _mediaPlayer.Volume;
        set => _mediaPlayer.Volume = Math.Clamp(value, 0, 100);
    }

    public double PlaybackRate => _requestedPlaybackRate;

    public Task LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("An audiobook path is required.", nameof(filePath));
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("The selected audiobook could not be found.", filePath);
        }

        if (!string.Equals(Path.GetExtension(filePath), ".m4b", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("This preliminary player currently opens M4B files only.");
        }

        RaiseState(PlaybackState.Loading);
        _hasReachedEnd = false;
        _mediaPlayer.Stop();

        var nextMedia = new Media(_libVlc, filePath, FromType.FromPath);
        var previousMedia = _currentMedia;
        _currentMedia = nextMedia;
        _mediaPlayer.Media = nextMedia;
        previousMedia?.Dispose();

        CurrentFilePath = Path.GetFullPath(filePath);
        RaiseProgress(TimeSpan.Zero, TimeSpan.Zero);
        RaiseState(PlaybackState.Ready);

        return Task.CompletedTask;
    }

    public bool Play()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_currentMedia is null)
        {
            return false;
        }

        _hasReachedEnd = false;
        var started = _mediaPlayer.Play();
        if (!started)
        {
            RaiseState(PlaybackState.Error, "VLC could not start the selected audiobook.");
        }

        return started;
    }

    public void Pause()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _mediaPlayer.SetPause(true);
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _mediaPlayer.Stop();
    }

    public void Seek(TimeSpan position)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_currentMedia is null)
        {
            return;
        }

        var maximum = Duration > TimeSpan.Zero ? Duration : TimeSpan.MaxValue;
        var clampedPosition = position < TimeSpan.Zero
            ? TimeSpan.Zero
            : position > maximum
                ? maximum
                : position;

        _mediaPlayer.Time = (long)clampedPosition.TotalMilliseconds;
        RaiseProgress(clampedPosition, Duration);
    }

    public bool TrySetPlaybackRate(double rate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (rate is < 0.5d or > 3d)
        {
            return false;
        }

        _requestedPlaybackRate = rate;
        return _currentMedia is null || _mediaPlayer.SetRate((float)rate) == 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _mediaPlayer.Opening -= OnOpening;
        _mediaPlayer.Playing -= OnPlaying;
        _mediaPlayer.Paused -= OnPaused;
        _mediaPlayer.Stopped -= OnStopped;
        _mediaPlayer.EndReached -= OnEndReached;
        _mediaPlayer.EncounteredError -= OnEncounteredError;
        _mediaPlayer.TimeChanged -= OnTimeChanged;
        _mediaPlayer.LengthChanged -= OnLengthChanged;

        _mediaPlayer.Stop();
        _mediaPlayer.Dispose();
        _currentMedia?.Dispose();
        _libVlc.Dispose();
    }

    private void OnOpening(object? sender, EventArgs e) => RaiseState(PlaybackState.Loading);

    private void OnPlaying(object? sender, EventArgs e)
    {
        _hasReachedEnd = false;
        _mediaPlayer.SetRate((float)_requestedPlaybackRate);
        RaiseState(PlaybackState.Playing);
        RaiseProgress(Position, Duration);
    }

    private void OnPaused(object? sender, EventArgs e) => RaiseState(PlaybackState.Paused);

    private void OnStopped(object? sender, EventArgs e)
    {
        if (!_hasReachedEnd)
        {
            RaiseState(PlaybackState.Stopped);
        }
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        _hasReachedEnd = true;
        RaiseProgress(Duration, Duration);
        RaiseState(PlaybackState.Ended);
    }

    private void OnEncounteredError(object? sender, EventArgs e) =>
        RaiseState(PlaybackState.Error, "VLC encountered an error while playing this audiobook.");

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e) =>
        RaiseProgress(TimeSpan.FromMilliseconds(Math.Max(0, e.Time)), Duration);

    private void OnLengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e) =>
        RaiseProgress(Position, TimeSpan.FromMilliseconds(Math.Max(0, e.Length)));

    private void RaiseProgress(TimeSpan position, TimeSpan duration) =>
        ProgressChanged?.Invoke(this, new PlaybackProgressChangedEventArgs(position, duration));

    private void RaiseState(PlaybackState state, string? message = null) =>
        StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(state, message));
}
