using ListenShelf.Application.Library;
using ListenShelf.Application.Playback;
using ListenShelf.Application.Settings;
using ListenShelf.Desktop.Services;
using ListenShelf.Desktop.ViewModels;
using ListenShelf.Infrastructure.Bookmarks;
using ListenShelf.Infrastructure.Library;
using ListenShelf.Infrastructure.Progress;
using ListenShelf.Infrastructure.Settings;
using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Tests;

public sealed class ChapterSearchTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(599, 0)]
    [InlineData(600, 1)]
    [InlineData(6600, 11)]
    public async Task SeekingWithoutNativeChapterEvents_UpdatesSelectionAndNavigationImmediately(double seconds, int expectedIndex)
    {
        using var session = new PlayerSession();
        await session.LoadAsync();
        session.Model.PositionSeconds = seconds == 0 ? 1 : 0;
        session.Model.SelectedChapter = session.Model.Chapters[^1];
        session.Model.IsPlaying = false;
        session.Model.ChapterSearchText = "storm";
        session.Engine.ResetCalls();
        var notifications = new HashSet<string?>();
        var previousChanges = 0;
        var nextChanges = 0;
        session.Model.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        session.Model.PreviousChapterCommand.CanExecuteChanged += (_, _) => previousChanges++;
        session.Model.NextChapterCommand.CanExecuteChanged += (_, _) => nextChanges++;

        // After completion, LibVLC reports the requested time but does not
        // emit a new chapter event until playback starts again.
        session.Model.PositionSeconds = seconds;

        Assert.Equal(expectedIndex, session.Model.SelectedChapter?.Index);
        Assert.Equal($"Chapter {expectedIndex + 1} of 12", session.Model.ChapterPositionText);
        Assert.Equal(expectedIndex > 0, session.Model.PreviousChapterCommand.CanExecute(null));
        Assert.Equal(expectedIndex < 11, session.Model.NextChapterCommand.CanExecute(null));
        Assert.Contains(nameof(MainWindowViewModel.ChapterPositionText), notifications);
        Assert.True(previousChanges > 0);
        Assert.True(nextChanges > 0);
        Assert.False(session.Model.IsPlaying);
        Assert.Equal("storm", session.Model.ChapterSearchText);
        Assert.Empty(session.Engine.SelectedIndexes);
        Assert.Equal(1, session.Engine.OtherPlaybackChanges);
    }

    [Theory]
    [InlineData("storm", "2,12")]
    [InlineData("STORM", "2,12")]
    [InlineData("  StOrM  ", "2,12")]
    [InlineData("arrival", "3")]
    [InlineData("2", "2,12")]
    [InlineData("12", "12")]
    [InlineData("1", "1,10,11,12")]
    [InlineData("0", "10")]
    [InlineData("missing", "")]
    [InlineData("0:00", "")]
    [InlineData("", "1,2,3,4,5,6,7,8,9,10,11,12")]
    [InlineData("  \t", "1,2,3,4,5,6,7,8,9,10,11,12")]
    [InlineData(null, "1,2,3,4,5,6,7,8,9,10,11,12")]
    public async Task Search_MatchesTitlesAndDisplayedNumbersWithoutChangingSelection(string? query, string expected)
    {
        using var session = new PlayerSession();
        await session.LoadAsync();
        var originalChapters = session.Model.Chapters.ToArray();
        var current = session.Model.SelectedChapter;
        session.Model.IsPlaying = true;

        session.Model.ChapterSearchText = query!;

        Assert.Equal(expected, string.Join(",", session.Model.ChapterSearchResults.Select(result => result.Chapter.Index + 1)));
        Assert.Equal(originalChapters, session.Model.Chapters.ToArray());
        Assert.Same(current, session.Model.SelectedChapter);
        foreach (var result in session.Model.ChapterSearchResults)
            Assert.Same(originalChapters[result.Chapter.Index], result.Chapter);
        Assert.Equal(expected.Length > 0, session.Model.HasChapterSearchResults);
        Assert.Equal(expected.Length == 0, session.Model.HasNoChapterSearchResults);
        Assert.Equal(!string.IsNullOrWhiteSpace(query), session.Model.HasChapterSearchText);
        Assert.True(session.Model.IsPlaying);
        session.AssertNoNavigation();
    }

    [Fact]
    public async Task Clear_RestoresCountsAndOrderWithoutChangingCurrentChapter()
    {
        using var session = new PlayerSession();
        await session.LoadAsync();
        var changes = new HashSet<string?>();
        session.Model.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        session.Model.IsChapterSearchExpanded = true;
        session.Model.ChapterSearchText = "storm";
        Assert.Equal("2 of 12 chapters", session.Model.ChapterSearchCountText);
        session.Model.ChapterSearchText = "no match";
        Assert.Equal("0 of 12 chapters", session.Model.ChapterSearchCountText);

        session.Model.ClearChapterSearchCommand.Execute(null);

        Assert.Empty(session.Model.ChapterSearchText);
        Assert.Equal("12 chapters", session.Model.ChapterSearchCountText);
        Assert.Equal(12, session.Model.ChapterSearchResults.Count);
        Assert.True(session.Model.IsChapterSearchExpanded);
        Assert.Equal(1, session.Model.SelectedChapter?.Index);
        Assert.Contains(nameof(MainWindowViewModel.HasChapterSearchText), changes);
        Assert.Contains(nameof(MainWindowViewModel.HasChapterSearchResults), changes);
        Assert.Contains(nameof(MainWindowViewModel.HasNoChapterSearchResults), changes);
        Assert.Contains(nameof(MainWindowViewModel.ChapterSearchCountText), changes);
        session.AssertNoNavigation();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChoosingResult_UsesOriginalChapterIndexAndNormalNavigationWithoutPlayPause(bool playing)
    {
        using var session = new PlayerSession();
        await session.LoadAsync();
        session.Model.IsPlaying = playing;
        session.Model.ChapterSearchText = "12";
        session.Model.ErrorMessage = "Previous error";

        Assert.Single(session.Model.ChapterSearchResults).GoToCommand.Execute(null);

        Assert.Equal([11], session.Engine.SelectedIndexes);
        Assert.Equal(playing, session.Model.IsPlaying);
        Assert.Equal("12", session.Model.ChapterSearchText);
        Assert.Empty(session.Model.ErrorMessage);
        Assert.Equal(0, session.Engine.OtherPlaybackChanges);
    }

    [Fact]
    public async Task PreviousNext_StillUseFullChapterListWhenSearchHasNoResults()
    {
        using var session = new PlayerSession();
        await session.LoadAsync();
        session.Model.ChapterSearchText = "missing";
        Assert.True(session.Model.PreviousChapterCommand.CanExecute(null));
        Assert.True(session.Model.NextChapterCommand.CanExecute(null));

        session.Model.PreviousChapterCommand.Execute(null);
        session.Model.NextChapterCommand.Execute(null);

        Assert.Equal([0, 2], session.Engine.SelectedIndexes);
        Assert.Equal("Chapter 2 of 12", session.Model.ChapterPositionText);
        Assert.Equal(0, session.Engine.OtherPlaybackChanges);
    }

    [Fact]
    public async Task MetadataChanges_ReapplyQueryAndInvalidateStaleResultObjects()
    {
        using var session = new PlayerSession();
        await session.LoadAsync();
        session.Model.ChapterSearchText = "storm";
        var stale = session.Model.ChapterSearchResults[0];
        session.Model.Chapters[1] = session.Model.Chapters[1] with { Title = "Renamed chapter" };
        Assert.Single(session.Model.ChapterSearchResults);
        stale.GoToCommand.Execute(null);
        session.AssertNoNavigation();

        session.Model.Chapters[1] = session.Model.Chapters[1] with { Title = "New storm" };
        Assert.Equal(2, session.Model.ChapterSearchResults.Count);
        Assert.Equal("storm", session.Model.ChapterSearchText);
        session.Model.Chapters.RemoveAt(11);
        Assert.Equal("1 of 11 chapters", session.Model.ChapterSearchCountText);
        Assert.Equal(1, Assert.Single(session.Model.ChapterSearchResults).Chapter.Index);
    }

    [Fact]
    public async Task Search_IsUnavailableWithoutChaptersWhileBusyOrAfterDispose()
    {
        using var session = new PlayerSession();
        Assert.False(session.Model.CanUseChapterSearch);
        Assert.False(session.Model.HasChapterSearchResults);
        Assert.Equal("0 chapters", session.Model.ChapterSearchCountText);
        await session.LoadAsync();
        var result = session.Model.ChapterSearchResults[0];
        session.Model.IsBusy = true;
        AssertUnavailable();
        session.Model.IsBusy = false;
        Assert.True(session.Model.CanUseChapterSearch);
        session.Model.IsFileLoaded = false;
        AssertUnavailable();
        session.Model.IsFileLoaded = true;
        session.Model.Chapters.Clear();
        AssertUnavailable();
        Assert.Empty(session.Model.ChapterSearchResults);
        session.AddChapters();
        session.Model.Dispose();
        AssertUnavailable();
        session.AssertNoNavigation();

        void AssertUnavailable()
        {
            Assert.False(session.Model.CanUseChapterSearch);
            result.GoToCommand.Execute(null);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NavigationFailure_IsReportedWithoutChangingPlayback(bool throws)
    {
        using var session = new PlayerSession();
        await session.LoadAsync();
        session.Engine.RejectSelection = !throws;
        session.Engine.ThrowOnSelection = throws;
        session.Model.IsPlaying = true;
        session.Model.ChapterSearchText = "12";

        Assert.Single(session.Model.ChapterSearchResults).GoToCommand.Execute(null);

        Assert.Contains("chapter could not be opened", session.Model.ErrorMessage);
        Assert.True(session.Model.IsPlaying);
        Assert.Equal(1, session.Model.SelectedChapter?.Index);
        Assert.Equal("12", session.Model.ChapterSearchText);
        Assert.Equal(0, session.Engine.OtherPlaybackChanges);
    }

    [Fact]
    public async Task LoadingAnotherBook_ResetsAndCollapsesSearchAndRejectsOldResults()
    {
        using var session = new PlayerSession();
        await session.LoadAsync();
        session.Model.ChapterSearchText = "storm";
        session.Model.IsChapterSearchExpanded = true;
        var stale = session.Model.ChapterSearchResults[0];
        session.Model.SelectedSection = AppSection.Settings;
        session.Model.ShowLibraryCommand.Execute(null);
        Assert.Equal("storm", session.Model.ChapterSearchText);
        Assert.True(session.Model.IsChapterSearchExpanded);
        var second = session.Library.Import(session.Workspace.CreateSourceFile("Second.mp3", [2])).Book;
        session.Model.ShowLibraryCommand.Execute(null);

        await session.Model.LibraryBooks.Single(item => item.Book.Id == second.Id).PlayCommand.ExecuteAsync(null);
        Assert.Empty(session.Model.ChapterSearchText);
        Assert.False(session.Model.IsChapterSearchExpanded);
        Assert.Empty(session.Model.ChapterSearchResults);
        session.AddChapters(); // Even value-equal chapter metadata belongs to the new book.
        session.Engine.ResetCalls();
        stale.GoToCommand.Execute(null);
        session.AssertNoNavigation();
        Assert.Equal("12 chapters", session.Model.ChapterSearchCountText);
    }

    [Fact]
    public async Task UnloadingBook_ResetsSearchWithoutLeavingChapterResults()
    {
        using var session = new PlayerSession();
        await session.LoadAsync();
        session.Model.ChapterSearchText = "storm";
        session.Model.IsChapterSearchExpanded = true;

        await Assert.Single(session.Model.LibraryBooks).RemoveBookCommand.ExecuteAsync(null);

        Assert.False(session.Model.IsFileLoaded);
        Assert.False(session.Model.CanUseChapterSearch);
        Assert.False(session.Model.IsChapterSearchExpanded);
        Assert.Empty(session.Model.ChapterSearchText);
        Assert.Empty(session.Model.ChapterSearchResults);
    }

    private sealed class PlayerSession : IDisposable
    {
        public TestWorkspace Workspace { get; } = new();
        public TestEngine Engine { get; } = new();
        public SqliteAudiobookLibrary Library { get; }
        public MainWindowViewModel Model { get; }

        public PlayerSession()
        {
            var database = new ListenShelfDatabase(Workspace.DatabasePath);
            Library = new SqliteAudiobookLibrary(database, Workspace.ManagedLibraryPath);
            Library.Import(Workspace.CreateSourceFile("Chapter search.m4b", [1]));
            Model = new MainWindowViewModel(
                Engine, filePickerService: null!, progressStore: new SqlitePlaybackProgressStore(database),
                bookmarkStore: new SqlitePlaybackBookmarkStore(database),
                appSettingsStore: new SqliteAppSettingsStore(database), themeService: new NoOpThemeService(),
                audiobookLibrary: Library, bookMetadataEditorService: null!, bookmarkEditorService: null!,
                bookRemovalConfirmationService: new ConfirmRemoval(), managedLibraryIntegrityChecker: null!,
                managedLibraryMaintenance: null!, libraryBackupService: null!, managedFileVerifier: null!,
                managedFileRepairer: null!, jumpToTimeService: null!, sleepTimerDurationService: null!);
        }

        public async Task LoadAsync()
        {
            await Assert.Single(Model.LibraryBooks).PlayCommand.ExecuteAsync(null);
            Assert.True(Model.IsFileLoaded);
            Assert.Empty(Model.ErrorMessage);
            AddChapters();
            Model.SelectedChapter = Model.Chapters[1];
            Engine.ResetCalls();
        }

        public void AddChapters()
        {
            for (var index = 0; index < 12; index++)
            {
                var title = index switch { 0 => "Opening", 1 => "Storm warning", 2 => "Arrival", 11 => "Thunderstorm", _ => $"Section {(char)('A' + index)}" };
                Model.Chapters.Add(new(index, title, TimeSpan.FromMinutes(index * 10), TimeSpan.FromMinutes(10)));
            }
        }

        public void AssertNoNavigation()
        {
            Assert.Empty(Engine.SelectedIndexes);
            Assert.Equal(0, Engine.OtherPlaybackChanges);
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
        public TimeSpan Position => TimeSpan.FromSeconds(700);
        public TimeSpan Duration => TimeSpan.FromHours(3);
        public int Volume { get; set; }
        public double PlaybackRate => 1;
        public IReadOnlyList<AudioChapter> Chapters => [];
        public int CurrentChapterIndex => -1;
        public List<int> SelectedIndexes { get; } = [];
        public int OtherPlaybackChanges { get; private set; }
        public bool RejectSelection { get; set; }
        public bool ThrowOnSelection { get; set; }
        public Task LoadAsync(string filePath, CancellationToken cancellationToken = default)
        {
            CurrentFilePath = filePath;
            return Task.CompletedTask;
        }
        public void Unload() => OtherPlaybackChanges++;
        public bool Play() { OtherPlaybackChanges++; return true; }
        public void Pause() => OtherPlaybackChanges++;
        public void Stop() => OtherPlaybackChanges++;
        public void Seek(TimeSpan position) => OtherPlaybackChanges++;
        public bool TrySetPlaybackRate(double rate) => true;
        public bool TrySelectChapter(int chapterIndex)
        {
            if (ThrowOnSelection) throw new InvalidOperationException("Test chapter failure");
            if (RejectSelection) return false;
            SelectedIndexes.Add(chapterIndex);
            return true;
        }
        public void ResetCalls() { SelectedIndexes.Clear(); OtherPlaybackChanges = 0; }
        public void Dispose() { }
    }
}
