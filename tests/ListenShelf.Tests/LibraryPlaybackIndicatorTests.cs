using ListenShelf.Application.Library;
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

public sealed partial class LibraryPlaybackIndicatorTests
{
    [Theory]
    [InlineData(PlaybackState.Ready, "Current · Ready")]
    [InlineData(PlaybackState.Playing, "Current · Playing")]
    [InlineData(PlaybackState.Paused, "Current · Paused")]
    [InlineData(PlaybackState.Stopped, "Current · Stopped")]
    [InlineData(PlaybackState.Ended, "Current · Finished")]
    [InlineData(PlaybackState.Error, "Current · Error")]
    [InlineData(PlaybackState.Loading, "Current · Loading")]
    public void Item_LabelsCurrentStateAndClearsItWithoutChangingBookOrCardSize(PlaybackState state, string text)
    {
        using var session = new PlayerSession();
        var item = session.Item(session.First);
        var originalBook = item.Book;
        var originalProgress = item.Progress;
        var size = (item.TileWidth, item.TileHeight, item.TileArtworkWidth, item.TileArtworkHeight);
        var notifications = new HashSet<string?>();
        item.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        item.UpdatePlaybackIndicator(true, state);

        Assert.True(item.IsCurrentBook);
        Assert.Equal(state == PlaybackState.Playing, item.IsCurrentBookPlaying);
        Assert.Equal(text, item.PlaybackIndicatorText);
        Assert.Contains(item.Title, item.PlaybackIndicatorToolTip);
        Assert.Same(originalBook, item.Book);
        Assert.Same(originalProgress, item.Progress);
        Assert.Equal(size, (item.TileWidth, item.TileHeight, item.TileArtworkWidth, item.TileArtworkHeight));
        Assert.Contains(nameof(LibraryBookItemViewModel.IsCurrentBook), notifications);
        Assert.Contains(nameof(LibraryBookItemViewModel.IsCurrentBookPlaying), notifications);
        Assert.Contains(nameof(LibraryBookItemViewModel.PlaybackIndicatorText), notifications);
        Assert.Contains(nameof(LibraryBookItemViewModel.PlaybackIndicatorToolTip), notifications);

        item.UpdatePlaybackIndicator(false, state);
        Assert.False(item.IsCurrentBook);
        Assert.False(item.IsCurrentBookPlaying);
        Assert.Empty(item.PlaybackIndicatorText);
        Assert.Empty(item.PlaybackIndicatorToolTip);
        Assert.Equal(0, session.Engine.PlaybackChanges);
    }

    [Fact]
    public void Item_UnchangedAndNonCurrentStatesDoNotRaiseRedundantNotifications()
    {
        using var session = new PlayerSession();
        var item = session.Item(session.First);
        var count = 0;
        item.PropertyChanged += (_, _) => count++;
        item.UpdatePlaybackIndicator(false, PlaybackState.Playing);
        item.UpdatePlaybackIndicator(false, PlaybackState.Paused);
        Assert.Equal(0, count);
        item.UpdatePlaybackIndicator(true, PlaybackState.Playing);
        Assert.True(count > 0);
        count = 0;
        item.UpdatePlaybackIndicator(true, PlaybackState.Playing);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task LoadingAndPlayPause_KeepExactlyOneCurrentBookAndUpdateLive()
    {
        using var session = new PlayerSession();
        Assert.DoesNotContain(session.Model.LibraryBooks, book => book.IsCurrentBook);
        await session.LoadAsync(session.First);
        var current = Assert.Single(session.Model.LibraryBooks, book => book.IsCurrentBook);
        Assert.Equal(session.First.Id, current.Book.Id);
        Assert.Equal("Current · Ready", current.PlaybackIndicatorText);
        Assert.False(current.IsCurrentBookPlaying);
        var storedProgress = session.ProgressStore.Get(session.First.FilePath);

        session.Model.IsPlaying = true;
        Assert.Equal("Current · Playing", current.PlaybackIndicatorText);
        Assert.True(current.IsCurrentBookPlaying);
        session.Model.IsPlaying = false;
        Assert.Equal("Current · Paused", current.PlaybackIndicatorText);
        Assert.False(current.IsCurrentBookPlaying);
        Assert.False(session.Item(session.Second).IsCurrentBook);
        Assert.Equal(storedProgress, session.ProgressStore.Get(session.First.FilePath));
        Assert.Equal(0, session.Engine.PlaybackChanges);
    }

    [Theory]
    [InlineData(LibraryViewMode.List)]
    [InlineData(LibraryViewMode.Tiles)]
    public async Task RefreshSortFilterAndOpenedGroups_PreserveTheSameCurrentBook(LibraryViewMode view)
    {
        using var session = new PlayerSession();
        await session.LoadAsync(session.First);
        session.Model.IsPlaying = true;
        var previousItem = session.Item(session.First);
        session.Model.SelectedLibraryView = view;
        session.Model.SelectedLibraryGroupOption = session.Model.LibraryGroupOptions.Single(option => option.Mode == LibraryGroupMode.Series);
        session.Model.SelectedLibrarySortOption = session.Model.LibrarySortOptions.Single(option => option.Mode == LibrarySortMode.Author);
        session.Model.ShowLibraryCommand.Execute(null);

        var current = session.Item(session.First);
        Assert.NotSame(previousItem, current);
        Assert.True(current.IsCurrentBookPlaying);
        Assert.Single(session.Model.LibraryGroups).OpenGroupCommand.Execute(null);
        Assert.Same(current, session.Model.ActiveLibraryGroupBooks.Single(book => book.IsCurrentBook));

        session.Model.LibrarySearchText = "Beta";
        Assert.DoesNotContain(session.Model.FilteredLibraryBooks, book => book.IsCurrentBook);
        Assert.True(current.IsCurrentBookPlaying);
        Assert.False(Assert.Single(session.Model.FilteredLibraryBooks).IsCurrentBook);
        session.Model.ClearLibrarySearchCommand.Execute(null);
        Assert.Same(current, session.Model.ActiveLibraryGroupBooks.Single(book => book.IsCurrentBook));
        session.Model.IsPlaying = false;
        Assert.Equal("Current · Paused", session.Model.ActiveLibraryGroupBooks.Single(book => book.IsCurrentBook).PlaybackIndicatorText);
        Assert.Equal(view, session.Model.SelectedLibraryView);
        Assert.Equal(0, session.Engine.PlaybackChanges);
    }

    [Fact]
    public async Task SameTitles_AreNotConfusedWithTheLoadedFile()
    {
        using var session = new PlayerSession();
        session.Library.UpdateMetadata(session.Second.Id, session.First.Metadata);
        session.Model.ShowLibraryCommand.Execute(null);
        Assert.All(session.Model.LibraryBooks, item => Assert.Equal("Alpha", item.Title));

        await session.LoadAsync(session.Second);

        Assert.Equal(session.Second.Id, Assert.Single(session.Model.LibraryBooks, item => item.IsCurrentBook).Book.Id);
        Assert.False(session.Item(session.First).IsCurrentBook);
    }

    [Fact]
    public async Task SwitchingBooks_ClearsOldBadgeDuringLoadingAndTransfersItWhenReady()
    {
        using var session = new PlayerSession();
        await session.LoadAsync(session.First);
        session.Model.IsPlaying = true;
        session.Engine.DuringLoad = () =>
        {
            Assert.False(session.Model.IsFileLoaded);
            Assert.DoesNotContain(session.Model.LibraryBooks, item => item.IsCurrentBook);
        };

        await session.LoadAsync(session.Second);

        var current = Assert.Single(session.Model.LibraryBooks, item => item.IsCurrentBook);
        Assert.Equal(session.Second.Id, current.Book.Id);
        Assert.Equal("Current · Ready", current.PlaybackIndicatorText);
        Assert.False(session.Item(session.First).IsCurrentBook);
        Assert.False(current.IsCurrentBookPlaying);
    }

    [Fact]
    public async Task FailedLoad_DoesNotLeaveAnOldOrNewCurrentBadge()
    {
        using var session = new PlayerSession();
        await session.LoadAsync(session.First);
        session.Model.IsPlaying = true;
        session.Engine.FailLoad = true;

        await session.Item(session.Second).PlayCommand.ExecuteAsync(null);

        Assert.False(session.Model.IsFileLoaded);
        Assert.DoesNotContain(session.Model.LibraryBooks, item => item.IsCurrentBook);
        Assert.Contains("Test load failure", session.Model.ErrorMessage);
        session.Model.ShowLibraryCommand.Execute(null);
        Assert.DoesNotContain(session.Model.LibraryBooks, item => item.IsCurrentBook);
    }

    [Fact]
    public async Task FailedStart_ShowsErrorForLoadedBookInsteadOfPlaying()
    {
        using var session = new PlayerSession();
        session.Engine.RejectPlay = true;

        await session.Item(session.First).PlayCommand.ExecuteAsync(null);

        Assert.True(session.Model.IsFileLoaded);
        var current = Assert.Single(session.Model.LibraryBooks, item => item.IsCurrentBook);
        Assert.Equal("Current · Error", current.PlaybackIndicatorText);
        Assert.False(current.IsCurrentBookPlaying);
        session.Model.ShowLibraryCommand.Execute(null);
        Assert.Equal("Current · Error", session.Item(session.First).PlaybackIndicatorText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemovingBooks_OnlyClearsBadgeWhenCurrentBookIsUnloaded(bool removeCurrent)
    {
        using var session = new PlayerSession();
        await session.LoadAsync(session.First);
        session.Model.IsPlaying = true;

        await session.Item(removeCurrent ? session.First : session.Second).RemoveBookCommand.ExecuteAsync(null);

        Assert.Equal(!removeCurrent, session.Model.IsFileLoaded);
        var remaining = Assert.Single(session.Model.LibraryBooks);
        Assert.Equal(!removeCurrent, remaining.IsCurrentBook);
        Assert.Equal(!removeCurrent, remaining.IsCurrentBookPlaying);
    }

    [Fact]
    public async Task Disposal_ClearsIndicatorsWithoutControllingAudio()
    {
        using var session = new PlayerSession();
        await session.LoadAsync(session.First);
        session.Model.IsPlaying = true;
        session.Model.Dispose();

        Assert.DoesNotContain(session.Model.LibraryBooks, item => item.IsCurrentBook);
        Assert.Equal(0, session.Engine.PlaybackChanges);
    }

    private sealed class PlayerSession : IDisposable
    {
        public TestWorkspace Workspace { get; } = new();
        public TestEngine Engine { get; } = new();
        public SqliteAudiobookLibrary Library { get; }
        public SqlitePlaybackProgressStore ProgressStore { get; }
        public LibraryBook First { get; }
        public LibraryBook Second { get; }
        public MainWindowViewModel Model { get; }

        public PlayerSession()
        {
            var database = new ListenShelfDatabase(Workspace.DatabasePath);
            Library = new SqliteAudiobookLibrary(database, Workspace.ManagedLibraryPath);
            ProgressStore = new SqlitePlaybackProgressStore(database);
            First = AddBook("Alpha", 1);
            Second = AddBook("Beta", 2);
            ProgressStore.Save(new PlaybackProgress(First.FilePath, TimeSpan.FromMinutes(10), TimeSpan.FromHours(3), DateTimeOffset.UtcNow));
            Model = new MainWindowViewModel(
                Engine, filePickerService: null!, progressStore: ProgressStore,
                bookmarkStore: new SqlitePlaybackBookmarkStore(database),
                appSettingsStore: new SqliteAppSettingsStore(database), themeService: new NoOpThemeService(),
                audiobookLibrary: Library, bookMetadataEditorService: null!, bookmarkEditorService: null!,
                bookRemovalConfirmationService: new ConfirmRemoval(), managedLibraryIntegrityChecker: null!,
                managedLibraryMaintenance: null!, libraryBackupService: null!, managedFileVerifier: null!,
                managedFileRepairer: null!, jumpToTimeService: null!, sleepTimerDurationService: null!);

            LibraryBook AddBook(string title, byte data)
            {
                var book = Library.Import(Workspace.CreateSourceFile($"{title}.m4b", [data])).Book;
                return Library.UpdateMetadata(book.Id, new AudiobookMetadata { Title = title, Authors = ["Shared author"], SeriesName = "Shared series" });
            }
        }

        public LibraryBookItemViewModel Item(LibraryBook book) => Model.LibraryBooks.Single(item => item.Book.Id == book.Id);

        public async Task LoadAsync(LibraryBook book)
        {
            await Item(book).PlayCommand.ExecuteAsync(null);
            Assert.True(Model.IsFileLoaded);
            Assert.Empty(Model.ErrorMessage);
            Engine.PlaybackChanges = 0;
            Engine.PlayCalls = 0;
            Engine.PauseCalls = 0;
        }

        public void Dispose() { Model.Dispose(); Workspace.Dispose(); }
    }

    private sealed class NoOpThemeService : IThemeService
    {
        public void ApplyTheme(AppTheme theme) { }
    }

    private sealed class ConfirmRemoval : IBookRemovalConfirmationService
    {
        public Task<bool> ConfirmRemovalAsync(LibraryBook book) => Task.FromResult(true);
    }

    private sealed class TestEngine : IAudioEngine
    {
        public event EventHandler<PlaybackProgressChangedEventArgs>? ProgressChanged { add { } remove { } }
        public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged { add { } remove { } }
        public event EventHandler<PlaybackChaptersChangedEventArgs>? ChaptersChanged { add { } remove { } }
        public string? CurrentFilePath { get; private set; }
        public TimeSpan Position => TimeSpan.Zero;
        public TimeSpan Duration => TimeSpan.FromHours(3);
        public int Volume { get; set; }
        public double PlaybackRate => 1;
        public IReadOnlyList<AudioChapter> Chapters => [];
        public int CurrentChapterIndex => -1;
        public int PlaybackChanges { get; set; }
        public int PlayCalls { get; set; }
        public int PauseCalls { get; set; }
        public int LoadCalls { get; private set; }
        public bool FailLoad { get; set; }
        public bool RejectPlay { get; set; }
        public bool ThrowOnPlay { get; set; }
        public bool ThrowOnPause { get; set; }
        public Action? DuringLoad { get; set; }
        public Task LoadAsync(string filePath, CancellationToken cancellationToken = default)
        {
            LoadCalls++;
            DuringLoad?.Invoke();
            if (FailLoad) throw new InvalidOperationException("Test load failure");
            CurrentFilePath = filePath;
            return Task.CompletedTask;
        }
        public void Unload() => PlaybackChanges++;
        public bool Play()
        {
            PlaybackChanges++;
            PlayCalls++;
            if (ThrowOnPlay) throw new InvalidOperationException("Test play failure");
            return !RejectPlay;
        }
        public void Pause()
        {
            PlaybackChanges++;
            PauseCalls++;
            if (ThrowOnPause) throw new InvalidOperationException("Test pause failure");
        }
        public void Stop() => PlaybackChanges++;
        public void Seek(TimeSpan position) => PlaybackChanges++;
        public bool TrySetPlaybackRate(double rate) => true;
        public bool TrySelectChapter(int chapterIndex) { PlaybackChanges++; return true; }
        public void Dispose() { }
    }
}
