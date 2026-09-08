using Android.Content;
using Android.OS;
using Android.Provider;
using ListenShelf.Application.Bookmarks;
using ListenShelf.Application.Library;
using ListenShelf.Application.Playback;
using ListenShelf.Application.Progress;
using ListenShelf.Infrastructure.Bookmarks;
using ListenShelf.Infrastructure.Library;
using ListenShelf.Infrastructure.Progress;
using ListenShelf.Infrastructure.Settings;
using ListenShelf.Infrastructure.Storage;
using ListenShelf.Playback.LibVlc;

namespace ListenShelf.Android;

// Process lifetime, independent of activity recreation. The foreground service owns active playback.
internal sealed class MobileSession
{
    private static MobileSession? _instance;
    public static Exception? StartupFailure { get; private set; }
    public static MobileSession Instance => _instance ?? throw new InvalidOperationException("ListenShelf has not initialized.");
    public static void Initialize(Context context)
    {
        if (_instance is not null) return;
        try { _instance = new MobileSession(context); StartupFailure = null; }
        catch (Exception ex) { StartupFailure = ex; global::Android.Util.Log.Error("ListenShelf", ex.ToString()); }
    }

    private readonly Context _context;
    private readonly Handler _handler = new(Looper.MainLooper!);
    private readonly SqlitePlaybackProgressStore _progress;
    private readonly SqlitePlaybackBookmarkStore _bookmarks;
    private readonly SqliteAppSettingsStore _settings;
    private readonly System.Threading.Timer _timer;
    private long? _sleepDeadline;
    private long _lastSave;
    private TimeSpan? _pendingPosition;
    private bool _hasPlaybackStarted;
    public LibVlcAudioEngine Engine { get; }
    public SqliteAudiobookLibrary Library { get; }
    public LibraryBook? CurrentBook { get; private set; }
    public PlaybackState State { get; private set; } = PlaybackState.Stopped;
    public string? Error { get; private set; }
    public bool IsLoading { get; private set; }
    public int BookmarkVersion { get; private set; }
    public event Action? Changed;
    public bool IsPlaying => State == PlaybackState.Playing;
    public TimeSpan Position => _pendingPosition ?? Engine.Position;
    public int RewindSeconds => _settings.GetRewindSeconds();
    public int ForwardSeconds => _settings.GetForwardSeconds();
    public TimeSpan? SleepRemaining => _sleepDeadline is { } end
        ? TimeSpan.FromMilliseconds(Math.Max(0, end - SystemClock.ElapsedRealtime())) : null;

    private MobileSession(Context context)
    {
        _context = context.ApplicationContext!;
        SQLitePCL.Batteries_V2.Init();
        // Android can expose /data/user/0 as a symlink inside an app's mount
        // namespace. Store canonical paths so shared library integrity checks
        // can continue rejecting links throughout the managed storage tree.
        var database = new ListenShelfDatabase(Path.Combine(_context.FilesDir!.CanonicalPath, "listenshelf.db"));
        Library = new SqliteAudiobookLibrary(database);
        _progress = new SqlitePlaybackProgressStore(database);
        _bookmarks = new SqlitePlaybackBookmarkStore(database);
        _settings = new SqliteAppSettingsStore(database);
        Engine = new LibVlcAudioEngine(() => LibVLCSharp.Shared.Core.Initialize());
        Engine.Volume = 100; // Phone volume buttons control listening volume.
        Engine.TrySetPlaybackRate(_settings.GetPlaybackRate());
        Engine.StateChanged += (_, args) => _handler.Post(() =>
        {
            State = args.State;
            if (!IsLoading && args.State == PlaybackState.Playing)
            {
                _hasPlaybackStarted = true;
                if (_pendingPosition is { } position)
                {
                    _pendingPosition = null;
                    Engine.Seek(position);
                }
            }
            Error = args.State == PlaybackState.Error ? args.Message ?? "Playback failed. Try reopening the book." : null;
            if (!IsLoading) SavePosition();
            Changed?.Invoke();
        });
        Engine.ChaptersChanged += (_, _) => _handler.Post(() => Changed?.Invoke());
        _timer = new System.Threading.Timer(_ => _handler.Post(Tick), null, 1000, 1000);
    }

    private void Tick()
    {
        if (_sleepDeadline is { } end && SystemClock.ElapsedRealtime() >= end)
        {
            _sleepDeadline = null;
            Pause();
        }
        if (IsPlaying && SystemClock.ElapsedRealtime() - _lastSave >= 5000) SavePosition();
        if (CurrentBook is not null) Changed?.Invoke();
    }

    public async Task RestoreAsync()
    {
        if (CurrentBook is not null) return;
        var last = _progress.GetMostRecent();
        var book = Library.GetBooks().FirstOrDefault(b => b.FilePath == last?.FilePath);
        if (book is not null && File.Exists(book.FilePath)) await LoadAsync(book);
    }

    public async Task LoadAsync(LibraryBook book)
    {
        if (IsLoading) return;
        if (CurrentBook?.Id == book.Id && Engine.CurrentFilePath == book.FilePath && State != PlaybackState.Error) return;
        SavePosition();
        IsLoading = true;
        Error = null;
        _sleepDeadline = null;
        try
        {
            Pause();
            CurrentBook = book;
            _hasPlaybackStarted = false;
            _pendingPosition = TimeSpan.Zero;
            Changed?.Invoke();
            var saved = _progress.Get(book.FilePath);
            await Engine.LoadAsync(book.FilePath);
            // VLC's metadata preload stops the native player. Retain the intended
            // position until actual playback starts, as the desktop host does.
            _pendingPosition = saved?.Position ?? TimeSpan.Zero;
            State = PlaybackState.Ready;
            SavePositionAfterLoad();
        }
        catch (Exception ex)
        {
            State = PlaybackState.Error;
            Error = "Unable to open this book: " + ex.Message;
            throw;
        }
        finally { IsLoading = false; Changed?.Invoke(); }
    }

    private void SavePositionAfterLoad()
    {
        if (CurrentBook is null) return;
        _progress.Save(new PlaybackProgress(CurrentBook.FilePath, Position, Engine.Duration, DateTimeOffset.UtcNow));
    }

    public void SavePosition()
    {
        if (IsLoading || CurrentBook is null || Engine.CurrentFilePath != CurrentBook.FilePath || State == PlaybackState.Error) return;
        try
        {
            SavePositionAfterLoad();
            _lastSave = SystemClock.ElapsedRealtime();
        }
        catch (Exception ex) { Error = "Could not save your position: " + ex.Message; }
    }

    public void Play()
    {
        if (CurrentBook is null || IsLoading) return;
        var intent = new Intent(_context, typeof(PlaybackService)).SetAction(PlaybackService.PlayAction);
        _context.StartForegroundService(intent);
    }

    internal void PlayWithAudioFocus()
    {
        if (!Engine.Play()) { Error = "Playback could not start. Try reopening the book."; State = PlaybackState.Error; }
        Changed?.Invoke();
    }

    public void Pause()
    {
        Engine.Pause();
        if (State is PlaybackState.Playing) State = PlaybackState.Paused;
        SavePosition();
        Changed?.Invoke();
    }

    public void Toggle() { if (IsPlaying) Pause(); else Play(); }
    public void Skip(int seconds) => Seek(Position + TimeSpan.FromSeconds(seconds));
    public void Seek(TimeSpan position)
    {
        if (IsLoading || CurrentBook is null) return;
        var clamped = TimeSpan.FromSeconds(Math.Clamp(position.TotalSeconds, 0, Math.Max(0, Engine.Duration.TotalSeconds)));
        if (!_hasPlaybackStarted) _pendingPosition = clamped;
        else Engine.Seek(clamped);
        SavePosition();
        Changed?.Invoke();
    }

    public void SelectChapter(int index)
    {
        if (IsLoading) return;
        if (Engine.Chapters.FirstOrDefault(c => c.Index == index) is { } chapter) Seek(chapter.Start);
    }

    public void SetRate(double rate)
    {
        if (!Engine.TrySetPlaybackRate(rate)) throw new InvalidOperationException("This playback speed is unavailable.");
        _settings.SavePlaybackRate(rate);
        Changed?.Invoke();
    }

    public void SetSleep(int? minutes)
    {
        _sleepDeadline = minutes is { } value ? SystemClock.ElapsedRealtime() + value * 60_000L : null;
        Changed?.Invoke();
    }

    public IReadOnlyList<PlaybackBookmark> GetBookmarks() => CurrentBook is { } book ? _bookmarks.GetForFile(book.FilePath) : [];
    public void AddBookmark()
    {
        if (CurrentBook is null) return;
        var chapter = Engine.Chapters.LastOrDefault(c => c.Start <= Position);
        var now = DateTimeOffset.UtcNow;
        _bookmarks.Save(new PlaybackBookmark(Guid.NewGuid(), CurrentBook.FilePath, Position,
            null, null, chapter?.Index, chapter?.Title, now, now));
        BookmarkVersion++;
        Changed?.Invoke();
    }

    public async Task<LibraryImportResult> ImportAsync(global::Android.Net.Uri uri,
        Action<string> report, CancellationToken token)
    {
        var resolver = _context.ContentResolver!;
        string name;
        using (var cursor = resolver.Query(uri, [IOpenableColumns.DisplayName], null, null, null))
            name = cursor is not null && cursor.MoveToFirst() ? cursor.GetString(0) ?? "book" : "book";
        name = Path.GetFileName(name.Replace('\\', '/'));
        if (!AudiobookFileFormats.IsSupported(name))
            throw new InvalidOperationException("Choose an M4B, M4A, or MP3 audiobook.");
        var staging = Path.Combine(_context.CacheDir!.CanonicalPath, "imports", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var source = Path.Combine(staging, name);
        try
        {
            await Task.Run(async () =>
            {
                using var input = resolver.OpenInputStream(uri) ?? throw new IOException("The selected file could not be opened.");
                using var output = File.Create(source);
                var buffer = new byte[128 * 1024];
                long copied = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, token)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    copied += read;
                    report($"Reading book · {copied / 1048576d:0.0} MB");
                }
            }, token);
            var progress = new Progress<LibraryImportProgress>(p => report($"{p.Stage} · {p.FileFraction:P0}"));
            return await Task.Run(() => Library.Import(source, progress, token), token);
        }
        finally
        {
            // Only this attempt's generated cache directory is a cleanup target.
            try { Directory.Delete(staging, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
