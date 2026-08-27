using ListenShelf.Application.Bookmarks;
using ListenShelf.Application.Playback;
using ListenShelf.Application.Progress;
using ListenShelf.Application.Settings;
using ListenShelf.Desktop.Services;
using ListenShelf.Desktop.ViewModels;
using ListenShelf.Infrastructure.Bookmarks;
using ListenShelf.Infrastructure.Library;
using ListenShelf.Infrastructure.Progress;
using ListenShelf.Infrastructure.Settings;
using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Tests;

public sealed partial class QuickBookmarkTests
{
    [Theory]
    [InlineData(false, 0d, "0:00")]
    [InlineData(true, 0d, "0:00")]
    [InlineData(false, 625.875d, "10:25")]
    [InlineData(true, 625.875d, "10:25")]
    [InlineData(false, 90061d, "25:01:01")]
    [InlineData(true, 90061d, "25:01:01")]
    public async Task QuickBookmark_SavesImmediatelyWithoutDialogOrPlaybackChanges(
        bool isPlaying, double seconds, string displayTime)
    {
        using var session = new PlayerSession();
        await session.LoadAsync();
        session.Model.IsPlaying = isPlaying;
        session.Engine.Position = TimeSpan.FromSeconds(seconds);
        session.Model.ErrorMessage = "Previous error";

        session.Model.QuickBookmarkCommand.Execute(null);

        var bookmark = Assert.Single(session.Store.GetForFile(session.FilePath));
        Assert.NotEqual(Guid.Empty, bookmark.Id);
        Assert.Equal(session.Engine.Position, bookmark.Position);
        Assert.Null(bookmark.Name);
        Assert.Null(bookmark.Note);
        Assert.Null(bookmark.ChapterIndex);
        Assert.Null(bookmark.ChapterTitle);
        Assert.Equal(bookmark.CreatedAtUtc, bookmark.UpdatedAtUtc);
        var item = Assert.Single(session.Model.Bookmarks);
        Assert.Equal($"Bookmark at {displayTime}", item.NameText);
        Assert.False(item.HasNote);
        Assert.True(session.Model.HasBookmarks);
        Assert.False(session.Model.HasNoBookmarks);
        Assert.Equal("1 bookmark", session.Model.BookmarkCountText);
        Assert.StartsWith("Bookmark saved at", session.Model.ProgressText);
        Assert.Empty(session.Model.ErrorMessage);
        Assert.Equal(isPlaying, session.Model.IsPlaying);
        session.AssertNoDialogOrPlaybackChanges();
    }

    [Theory]
    [InlineData(599.999d, 0, "Opening")]
    [InlineData(600d, 1, "Discovery")]
    [InlineData(1200d, 2, "Arrival")]
    public async Task QuickBookmark_UsesChapterAtPlayhead(double seconds, int index, string title)
    {
        using var session = new PlayerSession();
        await session.LoadAsync();
        session.AddChapters();
        session.Engine.Position = TimeSpan.FromSeconds(seconds);
        Assert.Null(session.Model.SelectedChapter);

        session.Model.QuickBookmarkCommand.Execute(null);

        var bookmark = Assert.Single(session.Store.GetForFile(session.FilePath));
        Assert.Equal(index, bookmark.ChapterIndex);
        Assert.Equal(title, bookmark.ChapterTitle);
        Assert.Contains($"Chapter {index + 1}: {title}", Assert.Single(session.Model.Bookmarks).LocationText);
        session.AssertNoDialogOrPlaybackChanges();
    }

    [Fact]
    public async Task QuickBookmark_UsesPendingResumeRatherThanEnginePosition()
    {
        var savedPosition = TimeSpan.FromSeconds(625);
        using var session = new PlayerSession(savedPosition);
        await session.LoadAsync();
        session.AddChapters();
        session.Model.IsPlaying = false;
        // The fake engine never signals Playing, so the saved position is still pending.
        // This is the same position source used while a restored book is ready but paused.
        Assert.Equal(TimeSpan.Zero, session.Engine.Position);

        session.Model.QuickBookmarkCommand.Execute(null);

        var bookmark = Assert.Single(session.Store.GetForFile(session.FilePath));
        Assert.Equal(savedPosition, bookmark.Position);
        Assert.Equal(1, bookmark.ChapterIndex);
        Assert.False(session.Model.IsPlaying);
        session.AssertNoDialogOrPlaybackChanges();
    }

    [Fact]
    public async Task QuickBookmark_CanBeEditedLaterAndReloadedWithItsIdentityAndPositionIntact()
    {
        using var session = new PlayerSession();
        await session.LoadAsync();
        session.AddChapters();
        session.Engine.Position = TimeSpan.FromSeconds(625);
        session.Model.QuickBookmarkCommand.Execute(null);
        var original = Assert.Single(session.Model.Bookmarks).Bookmark;
        session.AssertNoDialogOrPlaybackChanges();
        session.Editor.Result = new BookmarkEditResult("Favorite moment", "Listen to this again.");

        await Assert.Single(session.Model.Bookmarks).EditCommand.ExecuteAsync(null);

        Assert.Equal(1, session.Editor.ShowCount);
        Assert.Equal(original, session.Editor.LastBookmark);
        var reloaded = new SqlitePlaybackBookmarkStore(new ListenShelfDatabase(session.Workspace.DatabasePath));
        var updated = Assert.Single(reloaded.GetForFile(session.FilePath));
        Assert.Equal(original.Id, updated.Id);
        Assert.Equal(original.Position, updated.Position);
        Assert.Equal(original.CreatedAtUtc, updated.CreatedAtUtc);
        Assert.Equal(original.ChapterIndex, updated.ChapterIndex);
        Assert.Equal(original.ChapterTitle, updated.ChapterTitle);
        Assert.Equal("Favorite moment", updated.Name);
        Assert.Equal("Listen to this again.", updated.Note);
        Assert.Equal("Favorite moment", Assert.Single(session.Model.Bookmarks).NameText);
        Assert.True(Assert.Single(session.Model.Bookmarks).HasNote);
        Assert.Equal(0, session.Engine.PlaybackChanges);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EditorFirstBookmark_RemainsAvailableAndSupportsCancel(bool cancel)
    {
        using var session = new PlayerSession();
        await session.LoadAsync();
        session.Engine.Position = TimeSpan.FromSeconds(45);
        session.Editor.Result = cancel ? null : new BookmarkEditResult("Named first", "A note");

        await session.Model.AddBookmarkCommand.ExecuteAsync(null);

        Assert.Equal(1, session.Editor.ShowCount);
        Assert.Null(session.Editor.LastBookmark);
        if (cancel)
        {
            Assert.Empty(session.Store.GetForFile(session.FilePath));
        }
        else
        {
            var bookmark = Assert.Single(session.Store.GetForFile(session.FilePath));
            Assert.Equal("Named first", bookmark.Name);
            Assert.Equal("A note", bookmark.Note);
            Assert.Equal(TimeSpan.FromSeconds(45), bookmark.Position);
        }
        Assert.Equal(0, session.Engine.PlaybackChanges);
    }

    [Fact]
    public async Task QuickBookmark_DisablesWhileUnloadedBusyOrDisposedAndNotifiesAvailability()
    {
        using var session = new PlayerSession();
        var notifications = 0;
        session.Model.QuickBookmarkCommand.CanExecuteChanged += (_, _) => notifications++;
        AssertUnavailable();

        await session.LoadAsync();
        Assert.True(session.Model.QuickBookmarkCommand.CanExecute(null));
        session.Model.IsBusy = true;
        AssertUnavailable();
        session.Model.IsBusy = false;
        Assert.True(session.Model.QuickBookmarkCommand.CanExecute(null));
        session.Model.IsFileLoaded = false;
        AssertUnavailable();
        session.Model.IsFileLoaded = true;
        session.Model.Dispose();
        AssertUnavailable();

        Assert.True(notifications >= 6);
        Assert.Empty(session.Store.GetForFile(session.FilePath));
        session.AssertNoDialogOrPlaybackChanges();

        void AssertUnavailable()
        {
            Assert.False(session.Model.CanCreateBookmark);
            Assert.False(session.Model.QuickBookmarkCommand.CanExecute(null));
            // Explicit command invocation must be safe too, not just a disabled button.
            session.Model.QuickBookmarkCommand.Execute(null);
        }
    }

    [Fact]
    public async Task SaveFailure_ReportsErrorWithoutAddingPhantomBookmarkOrInterruptingPlayback()
    {
        using var session = new PlayerSession();
        await session.LoadAsync();
        session.Model.QuickBookmarkCommand.Execute(null);
        var existing = Assert.Single(session.Model.Bookmarks).Bookmark;
        using (var connection = session.Database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TRIGGER reject_bookmark BEFORE INSERT ON playback_bookmarks
                BEGIN SELECT RAISE(FAIL, 'Test bookmark write failure'); END;
                """;
            command.ExecuteNonQuery();
        }
        session.Model.ProgressText = "Listening";
        session.Model.IsPlaying = true;

        session.Model.QuickBookmarkCommand.Execute(null);

        Assert.Contains("The bookmark could not be saved", session.Model.ErrorMessage);
        Assert.Equal("Listening", session.Model.ProgressText);
        Assert.Equal(existing, Assert.Single(session.Store.GetForFile(session.FilePath)));
        Assert.Equal(existing, Assert.Single(session.Model.Bookmarks).Bookmark);
        Assert.True(session.Model.IsPlaying);
        session.AssertNoDialogOrPlaybackChanges();
    }

    [Fact]
    public async Task QuickBookmarks_RemainOrderedAndBelongToTheCorrectBook()
    {
        using var session = new PlayerSession();
        await session.LoadAsync();
        foreach (var seconds in new[] { 90, 20, 90 })
        {
            session.Engine.Position = TimeSpan.FromSeconds(seconds);
            session.Model.QuickBookmarkCommand.Execute(null);
        }
        Assert.Equal([20d, 90d, 90d], session.Model.Bookmarks.Select(item => item.Bookmark.Position.TotalSeconds));
        Assert.Equal(3, session.Model.Bookmarks.Select(item => item.Bookmark.Id).Distinct().Count());

        var second = session.Library.Import(session.Workspace.CreateSourceFile("Second.mp3", [2])).Book;
        session.Model.ShowLibraryCommand.Execute(null);
        await session.Model.LibraryBooks.Single(item => item.Book.Id == second.Id).PlayCommand.ExecuteAsync(null);
        session.Engine.PlaybackChanges = 0;
        session.Engine.Position = TimeSpan.FromSeconds(15);
        session.Model.QuickBookmarkCommand.Execute(null);

        Assert.Equal(3, session.Store.GetForFile(session.FilePath).Count);
        Assert.Equal(second.FilePath, Assert.Single(session.Model.Bookmarks).Bookmark.FilePath);
        Assert.Equal(TimeSpan.FromSeconds(15), Assert.Single(session.Store.GetForFile(second.FilePath)).Position);
        session.AssertNoDialogOrPlaybackChanges();
    }

    private sealed class PlayerSession : IDisposable
    {
        public TestWorkspace Workspace { get; } = new();
        public TestEngine Engine { get; } = new();
        public TestEditor Editor { get; } = new();
        public ListenShelfDatabase Database { get; }
        public SqlitePlaybackBookmarkStore Store { get; }
        public SqliteAudiobookLibrary Library { get; }
        public MainWindowViewModel Model { get; }
        public string FilePath { get; }

        public PlayerSession(TimeSpan? savedPosition = null, IBookRemovalConfirmationService? removalService = null)
        {
            Database = new ListenShelfDatabase(Workspace.DatabasePath);
            Store = new SqlitePlaybackBookmarkStore(Database);
            Library = new SqliteAudiobookLibrary(Database, Workspace.ManagedLibraryPath);
            FilePath = Library.Import(Workspace.CreateSourceFile("Quick bookmarks.m4b", [1])).Book.FilePath;
            var progressStore = new SqlitePlaybackProgressStore(Database);
            if (savedPosition is { } position)
            {
                progressStore.Save(new PlaybackProgress(FilePath, position, Engine.Duration, DateTimeOffset.UtcNow));
            }
            Model = new MainWindowViewModel(
                Engine, filePickerService: null!, progressStore, bookmarkStore: Store,
                appSettingsStore: new SqliteAppSettingsStore(Database), themeService: new NoOpThemeService(),
                audiobookLibrary: Library, bookMetadataEditorService: null!, bookmarkEditorService: Editor,
                bookRemovalConfirmationService: removalService!, managedLibraryIntegrityChecker: null!,
                managedLibraryMaintenance: null!, libraryBackupService: null!, managedFileVerifier: null!,
                managedFileRepairer: null!, jumpToTimeService: null!, sleepTimerDurationService: null!);
        }

        public async Task LoadAsync()
        {
            // Use normal book loading with a fake engine: no audio, native runtime, or UI dispatcher needed.
            await Assert.Single(Model.LibraryBooks).PlayCommand.ExecuteAsync(null);
            Assert.True(Model.IsFileLoaded);
            Assert.Empty(Model.ErrorMessage);
            Engine.PlaybackChanges = 0;
        }

        public void AddChapters()
        {
            Model.Chapters.Add(new(0, "Opening", TimeSpan.Zero, TimeSpan.FromMinutes(10)));
            Model.Chapters.Add(new(1, "Discovery", TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10)));
            Model.Chapters.Add(new(2, "Arrival", TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(10)));
        }

        public void AssertNoDialogOrPlaybackChanges()
        {
            Assert.Equal(0, Editor.ShowCount);
            Assert.Equal(0, Engine.PlaybackChanges);
        }

        public void Dispose()
        {
            Model.Dispose();
            Workspace.Dispose();
        }
    }

    private sealed class TestEditor : IBookmarkEditorService
    {
        public BookmarkEditResult? Result { get; set; }
        public int ShowCount { get; private set; }
        public PlaybackBookmark? LastBookmark { get; private set; }
        public Task<BookmarkEditResult?> EditAsync(PlaybackBookmark? bookmark)
        {
            ShowCount++;
            LastBookmark = bookmark;
            return Task.FromResult(Result);
        }
    }

    private sealed class NoOpThemeService : IThemeService
    {
        public void ApplyTheme(AppTheme theme) { }
    }

    private sealed class TestEngine : IAudioEngine
    {
        public event EventHandler<PlaybackProgressChangedEventArgs>? ProgressChanged { add { } remove { } }
        public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged { add { } remove { } }
        public event EventHandler<PlaybackChaptersChangedEventArgs>? ChaptersChanged { add { } remove { } }
        public string? CurrentFilePath { get; private set; }
        public TimeSpan Position { get; set; }
        public TimeSpan Duration => TimeSpan.FromHours(30);
        public int Volume { get; set; }
        public double PlaybackRate => 1;
        public IReadOnlyList<AudioChapter> Chapters => [];
        public int CurrentChapterIndex => -1;
        public int PlaybackChanges { get; set; }
        public Task LoadAsync(string filePath, CancellationToken cancellationToken = default)
        {
            CurrentFilePath = filePath;
            Position = TimeSpan.Zero;
            return Task.CompletedTask;
        }
        public void Unload() => PlaybackChanges++;
        public bool Play() { PlaybackChanges++; return true; }
        public void Pause() => PlaybackChanges++;
        public void Stop() => PlaybackChanges++;
        public void Seek(TimeSpan position) { PlaybackChanges++; Position = position; }
        public bool TrySetPlaybackRate(double rate) => true;
        public bool TrySelectChapter(int chapterIndex) { PlaybackChanges++; return true; }
        public void Dispose() { }
    }
}
