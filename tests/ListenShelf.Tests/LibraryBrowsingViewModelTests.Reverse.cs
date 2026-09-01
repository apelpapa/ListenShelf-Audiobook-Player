using ListenShelf.Application.Settings;
using ListenShelf.Desktop.ViewModels;
using ListenShelf.Infrastructure.Settings;
using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Tests;

public sealed partial class LibraryBrowsingViewModelTests
{
    [Theory]
    [InlineData(LibraryViewMode.List)]
    [InlineData(LibraryViewMode.Tiles)]
    public void ReverseSort_PreservesFilteringOpenGroupAndPlayback(LibraryViewMode view)
    {
        using var workspace = new TestWorkspace();
        var library = SeedLibrary(workspace);
        using var model = CreateViewModel(workspace);
        model.SelectedLibraryView = view;
        model.SelectedLibraryGroupOption = model.LibraryGroupOptions.Single(option => option.Mode == LibraryGroupMode.Series);
        model.SelectedLibraryStatusOption = model.LibraryStatusOptions.Single(option => option.Filter == LibraryStatusFilter.InProgress);
        model.LibrarySearchText = "Test Author";
        model.IsPlaying = true; // The idle engine throws on any playback operation.
        Assert.Single(model.LibraryGroups).OpenGroupCommand.Execute(null);
        var original = model.FilteredLibraryBooks.ToArray();
        var catalog = library.GetBooks().ToArray();

        model.IsLibrarySortReversed = true;

        Assert.Equal(original.Reverse(), model.FilteredLibraryBooks);
        Assert.Equal(["Middle", "Alpha"], Titles(model.ActiveLibraryGroupBooks));
        Assert.Equal("Middle", Assert.Single(model.LibraryGroups).PreviewTitle);
        Assert.True(model.IsLibraryGroupDetailVisible);
        Assert.Equal("B series", model.ActiveLibraryGroupName);
        Assert.Equal("Test Author", model.LibrarySearchText);
        Assert.Equal("2 of 4 audiobooks", model.LibraryBookCountText);
        Assert.Equal(LibraryStatusFilter.InProgress, model.SelectedLibraryStatusOption.Filter);
        Assert.Equal(view, model.SelectedLibraryView);
        Assert.True(model.IsPlaying);
        Assert.Equal(catalog.Select(book => book.Id), library.GetBooks().Select(book => book.Id));

        model.IsLibrarySortReversed = false;
        Assert.Equal(original, model.FilteredLibraryBooks);
        Assert.Equal(["Alpha", "Middle"], Titles(model.ActiveLibraryGroupBooks));
    }

    [Fact]
    public void ReverseSort_ReordersStackOverviewByItsFirstBook()
    {
        using var workspace = new TestWorkspace();
        SeedLibrary(workspace);
        using var model = CreateViewModel(workspace);
        model.SelectedLibraryGroupOption = model.LibraryGroupOptions.Single(option => option.Mode == LibraryGroupMode.Series);
        model.SelectedLibrarySortOption = model.LibrarySortOptions.Single(option => option.Mode == LibrarySortMode.SeriesOrder);

        model.IsLibrarySortReversed = true;

        Assert.Equal(["C series", "B series", "A series"], model.LibraryGroups.Select(group => group.Name));
        Assert.Equal(["Alpha", "Middle"], Titles(model.LibraryGroups[1].Books));
        Assert.Equal("Alpha", model.LibraryGroups[1].PreviewTitle);
    }

    [Fact]
    public void ReverseSort_RemainsSelectedThroughSortChangesRefreshAndClearingFilters()
    {
        using var workspace = new TestWorkspace();
        SeedLibrary(workspace);
        using var model = CreateViewModel(workspace);
        model.IsLibrarySortReversed = true;
        model.SelectedLibrarySortOption = model.LibrarySortOptions.Single(option => option.Mode == LibrarySortMode.RecentlyPlayed);
        Assert.Equal(["Unstarted", "Zebra", "Alpha", "Middle"], Titles(model.FilteredLibraryBooks));

        model.LibrarySearchText = "no matching book";
        Assert.Empty(model.FilteredLibraryBooks);
        model.ClearLibraryFiltersCommand.Execute(null);
        model.ShowLibraryCommand.Execute(null);

        Assert.True(model.IsLibrarySortReversed);
        Assert.Equal(LibrarySortMode.RecentlyPlayed, model.SelectedLibrarySortOption.Mode);
        Assert.Equal(["Unstarted", "Zebra", "Alpha", "Middle"], Titles(model.FilteredLibraryBooks));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReverseSort_IsAppliedOnNextLaunch(bool reverse)
    {
        using var workspace = new TestWorkspace();
        SeedLibrary(workspace);
        using (var model = CreateViewModel(workspace))
        {
            model.SelectedLibrarySortOption = model.LibrarySortOptions.Single(option => option.Mode == LibrarySortMode.Progress);
            model.IsLibrarySortReversed = !reverse;
            model.IsLibrarySortReversed = reverse;
        }

        using var reopened = CreateViewModel(workspace);

        Assert.Equal(reverse, reopened.IsLibrarySortReversed);
        Assert.Equal(LibrarySortMode.Progress, reopened.SelectedLibrarySortOption.Mode);
        Assert.Equal(reverse ? ["Unstarted", "Middle", "Alpha", "Zebra"] : new[] { "Zebra", "Alpha", "Middle", "Unstarted" }, Titles(reopened.FilteredLibraryBooks));
    }

    [Fact]
    public void ReverseSort_EmptyLibraryCanRememberTheChoice()
    {
        using var workspace = new TestWorkspace();
        using var model = CreateViewModel(workspace);
        Assert.False(model.IsLibrarySortReversed);
        model.IsLibrarySortReversed = true;
        Assert.Empty(model.FilteredLibraryBooks);
        Assert.True(model.IsLibraryEmpty);
        SeedLibrary(workspace);
        model.ShowLibraryCommand.Execute(null);
        Assert.Equal(["Zebra", "Unstarted", "Middle", "Alpha"], Titles(model.FilteredLibraryBooks));
    }

    [Fact]
    public void ReverseSort_NotifiesTooltipForDirectionAndSortChanges()
    {
        using var workspace = new TestWorkspace();
        using var model = CreateViewModel(workspace);
        var notifications = new List<string?>();
        model.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        Assert.Contains("Current order: title A–Z", model.LibrarySortDirectionToolTip);

        model.IsLibrarySortReversed = true;
        Assert.Contains(nameof(MainWindowViewModel.IsLibrarySortReversed), notifications);
        Assert.Contains(nameof(MainWindowViewModel.LibrarySortDirectionToolTip), notifications);
        Assert.Contains("Reverse order is on", model.LibrarySortDirectionToolTip);
        notifications.Clear();

        model.SelectedLibrarySortOption = model.LibrarySortOptions.Single(option => option.Mode == LibrarySortMode.DateAdded);
        Assert.Contains(nameof(MainWindowViewModel.LibrarySortDirectionToolTip), notifications);
        Assert.Contains("newest additions first", model.LibrarySortDirectionToolTip);
    }

    [Fact]
    public void ReverseSort_SaveFailureKeepsSessionOrderAndReportsItWithoutOverwritingPreference()
    {
        using var workspace = new TestWorkspace();
        SeedLibrary(workspace);
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        var settings = new SqliteAppSettingsStore(database);
        settings.SaveLibrarySortReversed(false);
        using var model = CreateViewModel(workspace);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TRIGGER reject_sort_update BEFORE UPDATE ON app_settings BEGIN SELECT RAISE(ABORT, 'Read-only test'); END;";
        command.ExecuteNonQuery();

        model.IsLibrarySortReversed = true;

        Assert.True(model.IsLibrarySortReversed);
        Assert.Equal(["Zebra", "Unstarted", "Middle", "Alpha"], Titles(model.FilteredLibraryBooks));
        Assert.Contains("Sort direction changed for this session, but could not be saved", model.LibraryStatusMessage);
        Assert.False(settings.GetLibrarySortReversed());
    }
}
