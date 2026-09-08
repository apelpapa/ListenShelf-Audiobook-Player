using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ListenShelf.Application.Bookmarks;
using ListenShelf.Application.Library;
using ListenShelf.Application.Playback;

namespace ListenShelf.Android;

public sealed partial class MobileViewModel : ObservableObject
{
    private readonly MobileSession _session;
    private bool _initialized;
    private bool _attached;
    private string? _coverPath;
    private CancellationTokenSource? _importCancellation;
    private IReadOnlyList<LibraryBook> _allBooks = [];
    private Guid? _bookmarkBookId;
    private int _bookmarkVersion = -1;
    [ObservableProperty] private bool _isLibrary = true;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isImporting;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _title = "Your next chapter awaits";
    [ObservableProperty] private string _author = "Add an audiobook to begin listening.";
    [ObservableProperty] private string _playLabel = "Play";
    [ObservableProperty] private string _stateLabel = "Ready when you are";
    [ObservableProperty] private string _positionText = "0:00";
    [ObservableProperty] private string _durationText = "0:00";
    [ObservableProperty] private string _remainingText = "";
    [ObservableProperty] private string _chapterText = "";
    [ObservableProperty] private string _sleepText = "Off";
    [ObservableProperty] private string _speedText = "1×";
    [ObservableProperty] private string _libraryCount = "YOUR AUDIOBOOKS";
    [ObservableProperty] private double _positionSeconds;
    [ObservableProperty] private double _durationSeconds = 1;
    [ObservableProperty] private bool _hasBook;
    [ObservableProperty] private bool _hasChapters;
    [ObservableProperty] private bool _hasBookmarks;
    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private string _emptyTitle = "A little shelf.\nA whole world.";
    [ObservableProperty] private string _emptyDescription = "Bring your favorite audiobooks.\nYour library and your place stay on this device.";
    [ObservableProperty] private Bitmap? _cover;
    public bool IsSeeking { get; set; }
    public ObservableCollection<BookItem> Books { get; } = [];
    public ObservableCollection<ChapterItem> Chapters { get; } = [];
    public ObservableCollection<BookmarkItem> Bookmarks { get; } = [];
    public string RewindLabel => $"↶  {_session.RewindSeconds}s";
    public string ForwardLabel => $"{_session.ForwardSeconds}s  ↷";
    public bool IsPlayer => !IsLibrary;
    public bool CanControl => HasBook && !IsBusy && !_session.IsLoading;
    public bool ShowMiniPlayer => HasBook && IsLibrary;
    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);
    public bool HasCover => Cover is not null;
    internal MobileViewModel(MobileSession session) => _session = session;

    public async void Attach()
    {
        if (_attached) return;
        _attached = true;
        _session.Changed += OnSessionChanged;
        RefreshBooks();
        if (!_initialized)
        {
            _initialized = true;
            await GuardAsync(_session.RestoreAsync);
        }
        Update();
    }

    public void Detach()
    {
        _session.Changed -= OnSessionChanged;
        _attached = false;
        _session.SavePosition();
    }
    partial void OnIsLibraryChanged(bool value) { OnPropertyChanged(nameof(IsPlayer)); OnPropertyChanged(nameof(ShowMiniPlayer)); }
    partial void OnHasBookChanged(bool value) { OnPropertyChanged(nameof(CanControl)); OnPropertyChanged(nameof(ShowMiniPlayer)); }
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanControl));
    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(HasStatus));
    partial void OnCoverChanged(Bitmap? value) => OnPropertyChanged(nameof(HasCover));
    partial void OnSearchChanged(string value) => FilterBooks();

    private void OnSessionChanged() => Dispatcher.UIThread.Post(Update);
    private void Update()
    {
        HasBook = _session.CurrentBook is not null;
        Title = _session.CurrentBook?.Title ?? "Your next chapter awaits";
        Author = _session.CurrentBook?.Metadata.Authors.Count > 0
            ? string.Join(", ", _session.CurrentBook.Metadata.Authors) : HasBook ? "Local audiobook" : "Add an audiobook to begin listening.";
        PlayLabel = _session.IsPlaying ? "Pause" : "Play";
        StateLabel = _session.IsLoading ? "Opening audiobook…" : _session.IsPlaying ? "NOW PLAYING" : HasBook ? "READY TO CONTINUE" : "YOUR PLAYER";
        var engine = _session.Engine;
        if (!IsSeeking) PositionSeconds = _session.Position.TotalSeconds;
        DurationSeconds = Math.Max(1, engine.Duration.TotalSeconds);
        PositionText = FormatTime(_session.Position);
        DurationText = FormatTime(engine.Duration);
        var remaining = TimeSpan.FromSeconds(Math.Max(0, engine.Duration.TotalSeconds - _session.Position.TotalSeconds) / engine.PlaybackRate);
        RemainingText = engine.Duration > TimeSpan.Zero ? $"{FormatTime(remaining)} listening time left" : "";
        SpeedText = $"{engine.PlaybackRate:0.##}×";
        SleepText = _session.SleepRemaining is { } left ? FormatTime(left) : "Off";
        ChapterText = engine.Chapters.LastOrDefault(c => c.Start <= _session.Position)?.Title ?? "";
        if (!Chapters.Select(c => c.Chapter).SequenceEqual(engine.Chapters))
        {
            Chapters.Clear();
            foreach (var chapter in engine.Chapters) Chapters.Add(new ChapterItem(chapter));
        }
        HasChapters = Chapters.Count > 0;
        var coverPath = _session.CurrentBook?.CoverPath;
        if (_coverPath != coverPath)
        {
            _coverPath = coverPath;
            var old = Cover;
            try { Cover = coverPath is not null ? new Bitmap(coverPath) : null; }
            catch { Cover = null; }
            old?.Dispose();
        }
        RefreshBookmarks();
        OnPropertyChanged(nameof(CanControl));
        if (_session.Error is { } error) Status = error;
    }

    private void RefreshBooks()
    {
        _allBooks = _session.Library.GetBooks();
        FilterBooks();
    }
    private void FilterBooks()
    {
        foreach (var book in Books) book.Dispose();
        Books.Clear();
        foreach (var book in _allBooks.Where(b => LibraryBookSearch.Matches(b, Search))) Books.Add(new BookItem(book));
        IsEmpty = Books.Count == 0;
        LibraryCount = $"YOUR AUDIOBOOKS · {_allBooks.Count}";
        EmptyTitle = _allBooks.Count > 0 ? "No matching books" : "A little shelf.\nA whole world.";
        EmptyDescription = _allBooks.Count > 0 ? "Try another title, author, or filename." : "Bring your favorite audiobooks.\nYour library and your place stay on this device.";
    }
    private void RefreshBookmarks()
    {
        if (_bookmarkBookId == _session.CurrentBook?.Id && _bookmarkVersion == _session.BookmarkVersion) return;
        _bookmarkBookId = _session.CurrentBook?.Id;
        _bookmarkVersion = _session.BookmarkVersion;
        var marks = _session.GetBookmarks();
        Bookmarks.Clear();
        foreach (var mark in marks) Bookmarks.Add(new BookmarkItem(mark));
        HasBookmarks = Bookmarks.Count > 0;
    }

    [RelayCommand] private void ShowLibrary() => IsLibrary = true;
    [RelayCommand] private void ShowPlayer() => IsLibrary = false;
    [RelayCommand] private void DismissStatus() => Status = "";
    [RelayCommand] private void CancelImport() => _importCancellation?.Cancel();
    [RelayCommand] private async Task OpenBookAsync(BookItem item)
    {
        if (IsBusy) return;
        IsLibrary = false;
        await GuardAsync(() => _session.LoadAsync(item.Book));
    }
    [RelayCommand] private async Task ImportAsync()
    {
        if (IsBusy || MainActivity.Current is not { } activity) return;
        await GuardAsync(async () =>
        {
            var uri = await activity.PickBookAsync();
            if (uri is null) return;
            IsImporting = true;
            using var cancellation = new CancellationTokenSource();
            _importCancellation = cancellation;
            try
            {
                var result = await _session.ImportAsync(uri, text => Dispatcher.UIThread.Post(() =>
                {
                    if (IsImporting) Status = text;
                }), cancellation.Token);
                IsImporting = false;
                RefreshBooks();
                Status = result.WasAdded ? $"Added {result.Book.Title}" : "This audiobook is already on your shelf.";
                if (_session.CurrentBook is null) await _session.LoadAsync(result.Book);
            }
            catch (OperationCanceledException) { Status = "Import canceled."; }
            finally { _importCancellation = null; IsImporting = false; }
        });
    }
    [RelayCommand] private void TogglePlayback() => Guard(_session.Toggle);
    [RelayCommand] private void Rewind() => Guard(() => _session.Skip(-_session.RewindSeconds));
    [RelayCommand] private void Forward() => Guard(() => _session.Skip(_session.ForwardSeconds));
    [RelayCommand] private void SetSpeed(string speed) => Guard(() => _session.SetRate(double.Parse(speed, System.Globalization.CultureInfo.InvariantCulture)));
    [RelayCommand] private void SetSleep(string minutes) => Guard(() => _session.SetSleep(int.Parse(minutes) is var value && value > 0 ? value : null));
    [RelayCommand] private void AddBookmark() => Guard(() => { _session.AddBookmark(); Status = "Bookmark saved at " + PositionText; });
    [RelayCommand] private void GoToChapter(ChapterItem item) => Guard(() => _session.SelectChapter(item.Chapter.Index));
    [RelayCommand] private void GoToBookmark(BookmarkItem item) => Guard(() => _session.Seek(item.Bookmark.Position));
    public void CommitSeek(double seconds) => Guard(() => _session.Seek(TimeSpan.FromSeconds(seconds)));
    private void Guard(Action action) { try { action(); Update(); } catch (Exception ex) { Status = ex.Message; } }
    private async Task GuardAsync(Func<Task> action)
    {
        IsBusy = true;
        try { await action(); }
        catch (Exception ex) { Status = ex.Message; }
        finally { IsBusy = false; Update(); }
    }
    public static string FormatTime(TimeSpan value) => value.TotalHours >= 1 ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}" : $"{(int)value.TotalMinutes}:{value.Seconds:00}";
}

public sealed class BookItem(LibraryBook book) : IDisposable
{
    public LibraryBook Book { get; } = book;
    public string Title => Book.Title;
    public string Detail => Book.Metadata.Authors.Count > 0 ? string.Join(", ", Book.Metadata.Authors) : $"{Path.GetExtension(Book.FilePath).TrimStart('.').ToUpperInvariant()} · {Book.FileSizeBytes / 1048576d:0.#} MB";
    public Bitmap? Cover { get; } = LoadCover(book.CoverPath);
    private static Bitmap? LoadCover(string? path)
    {
        if (path is null) return null;
        try { using var stream = File.OpenRead(path); return Bitmap.DecodeToWidth(stream, 160); }
        catch { return null; }
    }
    public bool HasCover => Cover is not null;
    public void Dispose() => Cover?.Dispose();
}
public sealed record ChapterItem(AudioChapter Chapter)
{
    public string Title => Chapter.Title;
    public string Time => MobileViewModel.FormatTime(Chapter.Start);
}
public sealed record BookmarkItem(PlaybackBookmark Bookmark)
{
    public string Title => Bookmark.Name ?? "Bookmark at " + MobileViewModel.FormatTime(Bookmark.Position);
    public string Detail => Bookmark.ChapterTitle ?? "Saved listening position";
}
