using LibVLCSharp.Shared;
using LibVLCSharp.Shared.Structures;
using ListenShelf.Application.Library;
using ListenShelf.Application.Playback;

namespace ListenShelf.Playback.LibVlc;

public sealed class LibVlcAudioEngine : IAudioEngine
{
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _mediaPlayer;
    private Media? _currentMedia;
    private IReadOnlyList<AudioChapter> _chapters = [];
    private int _currentChapterIndex = -1;
    private double _requestedPlaybackRate = 1d;
    private TimeSpan? _restartPosition;
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
        _mediaPlayer.ChapterChanged += OnChapterChanged;

        Volume = 80;
    }

    public event EventHandler<PlaybackProgressChangedEventArgs>? ProgressChanged;

    public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;

    public event EventHandler<PlaybackChaptersChangedEventArgs>? ChaptersChanged;

    public string? CurrentFilePath { get; private set; }

    public TimeSpan Position =>
        _restartPosition ?? TimeSpan.FromMilliseconds(Math.Max(0, _mediaPlayer.Time));

    public TimeSpan Duration => TimeSpan.FromMilliseconds(Math.Max(0, _mediaPlayer.Length));

    public int Volume
    {
        get => _mediaPlayer.Volume;
        set => _mediaPlayer.Volume = Math.Clamp(value, 0, 100);
    }

    public double PlaybackRate => _requestedPlaybackRate;

    public IReadOnlyList<AudioChapter> Chapters => _chapters;

    public int CurrentChapterIndex => _currentChapterIndex;

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

        if (!AudiobookFileFormats.IsSupported(filePath))
        {
            throw new NotSupportedException("ListenShelf currently opens M4B, M4A, and MP3 audiobooks.");
        }

        LoadMedia(filePath);

        return Task.CompletedTask;
    }

    public bool Play()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_currentMedia is null)
        {
            return false;
        }

        if (_hasReachedEnd)
        {
            LoadMedia(CurrentFilePath!);
        }

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

        if (_hasReachedEnd)
        {
            _restartPosition = clampedPosition;
            RaiseProgress(clampedPosition, Duration);
            RaiseState(PlaybackState.Ready);
            return;
        }

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

    public bool TrySelectChapter(int chapterIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_currentMedia is null || chapterIndex < 0 || chapterIndex >= _chapters.Count)
        {
            return false;
        }

        if (_hasReachedEnd)
        {
            var chapter = _chapters[chapterIndex];
            _restartPosition = chapter.Start;
            SetChapters(_chapters, chapterIndex);
            RaiseProgress(chapter.Start, Duration);
            RaiseState(PlaybackState.Ready);
            return true;
        }

        try
        {
            _mediaPlayer.Chapter = chapterIndex;
            RefreshChapters(chapterIndex);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void LoadMedia(string filePath)
    {
        RaiseState(PlaybackState.Loading);
        _hasReachedEnd = false;
        _restartPosition = null;
        _mediaPlayer.Stop();

        var nextMedia = new Media(_libVlc, filePath, FromType.FromPath);
        var previousMedia = _currentMedia;
        _currentMedia = nextMedia;
        _mediaPlayer.Media = nextMedia;
        previousMedia?.Dispose();

        CurrentFilePath = Path.GetFullPath(filePath);
        SetChapters([], -1);
        RaiseProgress(TimeSpan.Zero, TimeSpan.Zero);
        RaiseState(PlaybackState.Ready);
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
        _mediaPlayer.ChapterChanged -= OnChapterChanged;

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
        RefreshChapters();
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
        _restartPosition = null;
        RaiseProgress(Duration, Duration);
        RaiseState(PlaybackState.Ended);
    }

    private void OnEncounteredError(object? sender, EventArgs e) =>
        RaiseState(PlaybackState.Error, "VLC encountered an error while playing this audiobook.");

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e) =>
        RaiseProgress(TimeSpan.FromMilliseconds(Math.Max(0, e.Time)), Duration);

    private void OnLengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e)
    {
        RefreshChapters();
        RaiseProgress(Position, TimeSpan.FromMilliseconds(Math.Max(0, e.Length)));
    }

    private void OnChapterChanged(object? sender, MediaPlayerChapterChangedEventArgs e) =>
        RefreshChapters(e.Chapter);

    private void RefreshChapters(int? currentChapterIndex = null)
    {
        if (_currentMedia is null)
        {
            SetChapters([], -1);
            return;
        }

        try
        {
            var descriptions = _mediaPlayer.FullChapterDescriptions(-1) ?? [];
            var chapterCount = Math.Max(_mediaPlayer.ChapterCount, descriptions.Length);
            if (chapterCount <= 0)
            {
                SetChapters([], -1);
                return;
            }

            var chapters = new List<AudioChapter>(chapterCount);
            for (var index = 0; index < chapterCount; index++)
            {
                ChapterDescription? description =
                    index < descriptions.Length ? descriptions[index] : null;
                var chapterName = description?.Name;
                var title = string.IsNullOrWhiteSpace(chapterName)
                    ? $"Chapter {index + 1}"
                    : chapterName.Trim();
                chapters.Add(new AudioChapter(
                    index,
                    title,
                    TimeSpan.FromMilliseconds(Math.Max(0L, description?.TimeOffset ?? 0L)),
                    TimeSpan.FromMilliseconds(Math.Max(0L, description?.Duration ?? 0L))));
            }

            var current = currentChapterIndex ?? _mediaPlayer.Chapter;
            SetChapters(chapters, current >= 0 && current < chapters.Count ? current : 0);
        }
        catch
        {
            SetChapters([], -1);
        }
    }

    private void SetChapters(IReadOnlyList<AudioChapter> chapters, int currentChapterIndex)
    {
        _chapters = chapters;
        _currentChapterIndex = currentChapterIndex;
        ChaptersChanged?.Invoke(
            this,
            new PlaybackChaptersChangedEventArgs(_chapters, _currentChapterIndex));
    }

    private void RaiseProgress(TimeSpan position, TimeSpan duration) =>
        ProgressChanged?.Invoke(this, new PlaybackProgressChangedEventArgs(position, duration));

    private void RaiseState(PlaybackState state, string? message = null) =>
        StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(state, message));
}
