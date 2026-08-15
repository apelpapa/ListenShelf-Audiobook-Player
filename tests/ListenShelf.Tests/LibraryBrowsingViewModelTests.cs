using ListenShelf.Application.Library;
using ListenShelf.Application.Playback;
using ListenShelf.Application.Progress;
using ListenShelf.Application.Settings;
using ListenShelf.Desktop.Services;
using ListenShelf.Desktop.ViewModels;
using ListenShelf.Infrastructure.Library;
using ListenShelf.Infrastructure.Progress;
using ListenShelf.Infrastructure.Settings;
using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Tests;

public sealed class LibraryBrowsingViewModelTests
{
    [Fact]
    public void FileVerificationAvailability_FollowsLibraryBackupAndStorageOperations()
    {
        using var workspace = new TestWorkspace();
        SeedLibrary(workspace);
        using var model = CreateViewModel(workspace);
        Assert.True(model.Verification.CanVerifyAll);
        Assert.False(model.Verification.HasResults);
        model.IsLibraryBusy = true;
        Assert.False(model.Verification.CanVerifyAll);
        model.IsLibraryBusy = false;
        model.IsBackupBusy = true;
        Assert.False(model.Verification.CanVerifyAll);
        model.IsBackupBusy = false;
        model.IsManagedStorageCheckRunning = true;
        Assert.False(model.Verification.CanVerifyAll);
        model.IsManagedStorageCheckRunning = false;
        Assert.True(model.Verification.CanVerifyAll);
    }

    [Theory]
    [InlineData(LibraryViewMode.List)]
    [InlineData(LibraryViewMode.Tiles)]
    public void SearchSortStatusAndGroups_ShareTheSameResults(LibraryViewMode viewMode)
    {
        using var workspace = new TestWorkspace();
        var library = SeedLibrary(workspace);
        using var viewModel = CreateViewModel(workspace);
        viewModel.SelectedLibraryView = viewMode;
        viewModel.SelectedLibrarySortOption = viewModel.LibrarySortOptions.Single(option =>
            option.Mode == LibrarySortMode.RecentlyPlayed);
        viewModel.SelectedLibraryStatusOption = viewModel.LibraryStatusOptions.Single(option =>
            option.Filter == LibraryStatusFilter.InProgress);
        viewModel.SelectedLibraryGroupOption = viewModel.LibraryGroupOptions.Single(option =>
            option.Mode == LibraryGroupMode.Series);

        Assert.Equal(["Middle", "Alpha"], Titles(viewModel.FilteredLibraryBooks));
        Assert.Equal("2 of 4 audiobooks", viewModel.LibraryBookCountText);
        var group = Assert.Single(viewModel.LibraryGroups);
        Assert.Equal("B series", group.Name);
        Assert.Equal("2 books", group.CountText);
        Assert.Equal("Middle", group.PreviewTitle);
        group.OpenGroupCommand.Execute(null);
        Assert.True(viewModel.IsLibraryGroupDetailVisible);
        Assert.Equal(["Middle", "Alpha"], Titles(viewModel.ActiveLibraryGroupBooks));

        viewModel.SelectedLibrarySortOption = viewModel.LibrarySortOptions.Single(option =>
            option.Mode == LibrarySortMode.Title);
        Assert.Equal(["Alpha", "Middle"], Titles(viewModel.ActiveLibraryGroupBooks));
        Assert.Equal("B series", viewModel.ActiveLibraryGroupName);
        Assert.Equal("Alpha", Assert.Single(viewModel.LibraryGroups).PreviewTitle);

        viewModel.LibrarySearchText = "middle";
        Assert.Equal(["Middle"], Titles(viewModel.ActiveLibraryGroupBooks));
        Assert.Equal("1 of 4 audiobooks", viewModel.LibraryBookCountText);
        Assert.Equal("1 book", viewModel.ActiveLibraryGroupCountText);

        viewModel.LibrarySearchText = "does not match";
        Assert.True(viewModel.IsLibrarySearchEmpty);
        Assert.False(viewModel.HasVisibleLibraryBooks);
        Assert.Empty(viewModel.LibraryGroups);
        Assert.Contains("in progress", viewModel.LibrarySearchEmptyDescription);

        viewModel.ClearLibraryFiltersCommand.Execute(null);
        Assert.False(viewModel.HasLibraryFilters);
        Assert.False(viewModel.IsLibrarySearchEmpty);
        Assert.Equal(4, viewModel.FilteredLibraryBooks.Count);
        Assert.Equal("4 audiobooks", viewModel.LibraryBookCountText);
        Assert.Equal(LibrarySortMode.Title, viewModel.SelectedLibrarySortOption.Mode);
        Assert.Equal(viewMode, viewModel.SelectedLibraryView);
        Assert.Equal(LibraryGroupMode.Series, viewModel.SelectedLibraryGroupOption.Mode);
        Assert.Equal(4, library.GetBooks().Count);
    }

    [Fact]
    public void StackOverview_UsesTheRankOfItsFirstMatchingBook()
    {
        using var workspace = new TestWorkspace();
        SeedLibrary(workspace);
        using var viewModel = CreateViewModel(workspace);
        viewModel.SelectedLibraryGroupOption = viewModel.LibraryGroupOptions.Single(option =>
            option.Mode == LibraryGroupMode.Series);
        viewModel.SelectedLibrarySortOption = viewModel.LibrarySortOptions.Single(option =>
            option.Mode == LibrarySortMode.RecentlyPlayed);

        Assert.Equal(["B series", "A series", "C series"], viewModel.LibraryGroups.Select(group => group.Name));
        Assert.Equal("Middle", viewModel.LibraryGroups[0].PreviewTitle);

        viewModel.SelectedLibrarySortOption = viewModel.LibrarySortOptions.Single(option =>
            option.Mode == LibrarySortMode.SeriesOrder);
        Assert.Equal(["A series", "B series", "C series"], viewModel.LibraryGroups.Select(group => group.Name));
        Assert.Equal(["Middle", "Alpha"], Titles(viewModel.LibraryGroups[1].Books));
    }

    [Fact]
    public void Choices_AreAppliedOnTheNextLaunch()
    {
        using var workspace = new TestWorkspace();
        SeedLibrary(workspace);
        using (var viewModel = CreateViewModel(workspace))
        {
            viewModel.SelectedLibrarySortOption = viewModel.LibrarySortOptions.Single(option =>
                option.Mode == LibrarySortMode.Progress);
            viewModel.SelectedLibraryStatusOption = viewModel.LibraryStatusOptions.Single(option =>
                option.Filter == LibraryStatusFilter.InProgress);
        }

        using var reopened = CreateViewModel(workspace);
        Assert.Equal(LibrarySortMode.Progress, reopened.SelectedLibrarySortOption.Mode);
        Assert.Equal(LibraryStatusFilter.InProgress, reopened.SelectedLibraryStatusOption.Filter);
        Assert.Equal(["Alpha", "Middle"], Titles(reopened.FilteredLibraryBooks));
    }

    [Fact]
    public void FinishedAndRewoundBooks_MoveBetweenStatusFiltersOnLibraryRefresh()
    {
        using var workspace = new TestWorkspace();
        var library = SeedLibrary(workspace);
        var middle = library.GetBooks().Single(book => book.Title == "Middle");
        var progressStore = new SqlitePlaybackProgressStore(new ListenShelfDatabase(workspace.DatabasePath));
        using var viewModel = CreateViewModel(workspace);
        viewModel.SelectedLibraryStatusOption = viewModel.LibraryStatusOptions.Single(option =>
            option.Filter == LibraryStatusFilter.InProgress);

        SavePosition(100);
        viewModel.ShowLibraryCommand.Execute(null);
        Assert.Equal(["Alpha"], Titles(viewModel.FilteredLibraryBooks));
        Assert.Equal("Finished", viewModel.LibraryBooks.Single(book => book.Book.Id == middle.Id).ProgressSummary);

        SavePosition(30);
        viewModel.ShowLibraryCommand.Execute(null);
        Assert.Equal(["Alpha", "Middle"], Titles(viewModel.FilteredLibraryBooks));
        Assert.StartsWith("In progress", viewModel.LibraryBooks.Single(book => book.Book.Id == middle.Id).ProgressSummary);

        void SavePosition(int seconds) => progressStore.Save(new PlaybackProgress(
            middle.FilePath, TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(100), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void EmptyStatusFilter_OffersARecoveryWithoutChangingSort()
    {
        using var workspace = new TestWorkspace();
        using var viewModel = CreateViewModel(workspace);
        Assert.True(viewModel.IsLibraryEmpty);
        Assert.False(viewModel.IsLibrarySearchEmpty);

        SeedLibrary(workspace);
        viewModel.ShowLibraryCommand.Execute(null);
        viewModel.SelectedLibraryStatusOption = viewModel.LibraryStatusOptions.Single(option =>
            option.Filter == LibraryStatusFilter.Finished);
        viewModel.LibrarySearchText = "Middle";
        Assert.True(viewModel.IsLibrarySearchEmpty);
        viewModel.ClearLibrarySearchCommand.Execute(null);
        Assert.Equal(["Zebra"], Titles(viewModel.FilteredLibraryBooks));
        Assert.True(viewModel.HasLibraryFilters);
    }

    private static SqliteAudiobookLibrary SeedLibrary(TestWorkspace workspace)
    {
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        var library = new SqliteAudiobookLibrary(database, workspace.ManagedLibraryPath);
        Add("Zebra", "A series", "2", 100, 1);
        Add("Middle", "B series", "2", 20, 3);
        Add("Alpha", "B series", "10", 50, 2);
        Add("Unstarted", "C series", "1", null, 1);
        return library;

        void Add(string title, string series, string number, int? position, int day)
        {
            var book = library.Import(workspace.CreateSourceFile($"{title}.m4b", System.Text.Encoding.UTF8.GetBytes(title))).Book;
            library.UpdateMetadata(book.Id, new AudiobookMetadata
            {
                Title = title,
                Authors = ["Test Author"],
                SeriesName = series,
                SeriesPosition = number,
            });
            if (position is { } seconds)
            {
                new SqlitePlaybackProgressStore(database).Save(new PlaybackProgress(
                    book.FilePath, TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(100),
                    new DateTimeOffset(2026, 9, day, 0, 0, 0, TimeSpan.Zero)));
            }
        }
    }

    private static MainWindowViewModel CreateViewModel(TestWorkspace workspace)
    {
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        // Library browsing does not use playback or dialog services.
        return new MainWindowViewModel(
            audioEngine: new IdleAudioEngine(), filePickerService: null!,
            progressStore: new SqlitePlaybackProgressStore(database), bookmarkStore: null!,
            appSettingsStore: new SqliteAppSettingsStore(database), themeService: new NoOpThemeService(),
            audiobookLibrary: new SqliteAudiobookLibrary(database, workspace.ManagedLibraryPath),
            bookMetadataEditorService: null!, bookmarkEditorService: null!, bookRemovalConfirmationService: null!,
            managedLibraryIntegrityChecker: null!, managedLibraryMaintenance: null!, libraryBackupService: null!,
            managedFileVerifier: new SqliteManagedFileVerifier(database, workspace.ManagedLibraryPath),
            managedFileRepairer: new SqliteAudiobookLibrary(database, workspace.ManagedLibraryPath));
    }

    private static string[] Titles(IEnumerable<LibraryBookItemViewModel> books) =>
        books.Select(book => book.Title).ToArray();

    private sealed class NoOpThemeService : IThemeService
    {
        public void ApplyTheme(AppTheme theme) { }
    }

    private sealed class IdleAudioEngine : IAudioEngine
    {
        public event EventHandler<PlaybackProgressChangedEventArgs>? ProgressChanged { add { } remove { } }
        public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged { add { } remove { } }
        public event EventHandler<PlaybackChaptersChangedEventArgs>? ChaptersChanged { add { } remove { } }
        public string? CurrentFilePath => null;
        public TimeSpan Position => TimeSpan.Zero;
        public TimeSpan Duration => TimeSpan.Zero;
        public int Volume { get; set; }
        public double PlaybackRate => 1;
        public IReadOnlyList<AudioChapter> Chapters => [];
        public int CurrentChapterIndex => -1;
        public Task LoadAsync(string filePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Unload() => throw new NotSupportedException();
        public bool Play() => throw new NotSupportedException();
        public void Pause() => throw new NotSupportedException();
        public void Stop() => throw new NotSupportedException();
        public void Seek(TimeSpan position) => throw new NotSupportedException();
        public bool TrySetPlaybackRate(double rate) => true;
        public bool TrySelectChapter(int chapterIndex) => throw new NotSupportedException();
        public void Dispose() { }
    }
}
