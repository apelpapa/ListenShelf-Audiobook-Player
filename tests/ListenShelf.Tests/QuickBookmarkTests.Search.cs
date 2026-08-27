using System.Globalization;
using ListenShelf.Application.Bookmarks;
using ListenShelf.Application.Library;
using ListenShelf.Desktop.Services;
using ListenShelf.Desktop.ViewModels;

namespace ListenShelf.Tests;

public sealed partial class QuickBookmarkTests
{
    [Theory]
    [InlineData("storm", "45,90")]
    [InlineData("STORM", "45,90")]
    [InlineData("  tOrM  ", "45,90")]
    [InlineData("warning", "90")]
    [InlineData("listen again", "20")]
    [InlineData("Arrival", "45")]
    [InlineData("Bookmark at", "90061")]
    [InlineData("25:01:01", "90061")]
    [InlineData("missing", "")]
    [InlineData("' OR 1=1--", "")]
    [InlineData("", "20,45,90,90061")]
    [InlineData("  \t ", "20,45,90,90061")]
    [InlineData(null, "20,45,90,90061")]
    public async Task Search_FiltersNamesNotesAndFallbackNamesWithoutChangingSourceOrPlayback(
        string? query, string expectedPositions)
    {
        using var session = new PlayerSession();
        SeedSearchBookmarks(session);
        await session.LoadAsync();
        session.Model.IsPlaying = true;
        var storedBefore = session.Store.GetForFile(session.FilePath);
        var itemsBefore = session.Model.Bookmarks.ToArray();

        session.Model.BookmarkSearchText = query!;

        Assert.Equal(expectedPositions, string.Join(",", session.Model.FilteredBookmarks.Select(
            item => item.Bookmark.Position.TotalSeconds.ToString(CultureInfo.InvariantCulture))));
        Assert.Equal(itemsBefore, session.Model.Bookmarks.ToArray());
        Assert.Equal(storedBefore, session.Store.GetForFile(session.FilePath));
        foreach (var item in session.Model.FilteredBookmarks)
        {
            Assert.Same(itemsBefore.Single(original => original.Bookmark.Id == item.Bookmark.Id), item);
        }
        Assert.True(session.Model.HasBookmarks);
        Assert.False(session.Model.HasNoBookmarks);
        Assert.Equal(expectedPositions.Length > 0, session.Model.HasMatchingBookmarks);
        Assert.Equal(expectedPositions.Length == 0, session.Model.HasNoMatchingBookmarks);
        Assert.Equal(!string.IsNullOrWhiteSpace(query), session.Model.HasBookmarkSearchText);
        Assert.True(session.Model.IsPlaying);
        session.AssertNoDialogOrPlaybackChanges();
    }

    [Fact]
    public async Task Search_ClearRestoresOrderCountsAndVisibilityAndNotifiesBindings()
    {
        using var session = new PlayerSession();
        SeedSearchBookmarks(session);
        await session.LoadAsync();
        var changed = new HashSet<string?>();
        session.Model.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        session.Model.IsPlaying = false;

        session.Model.BookmarkSearchText = "storm";
        Assert.Equal("2 of 4 bookmarks", session.Model.BookmarkCountText);
        session.Model.BookmarkSearchText = "no match";
        Assert.Equal("0 of 4 bookmarks", session.Model.BookmarkCountText);
        Assert.True(session.Model.HasNoMatchingBookmarks);
        session.Model.ClearBookmarkSearchCommand.Execute(null);

        Assert.Empty(session.Model.BookmarkSearchText);
        Assert.False(session.Model.HasBookmarkSearchText);
        Assert.False(session.Model.HasNoMatchingBookmarks);
        Assert.True(session.Model.HasMatchingBookmarks);
        Assert.Equal("4 bookmarks", session.Model.BookmarkCountText);
        Assert.Equal(session.Model.Bookmarks.ToArray(), session.Model.FilteredBookmarks.ToArray());
        Assert.Contains(nameof(MainWindowViewModel.HasBookmarkSearchText), changed);
        Assert.Contains(nameof(MainWindowViewModel.HasMatchingBookmarks), changed);
        Assert.Contains(nameof(MainWindowViewModel.HasNoMatchingBookmarks), changed);
        Assert.Contains(nameof(MainWindowViewModel.BookmarkCountText), changed);
        Assert.False(session.Model.IsPlaying);
        session.AssertNoDialogOrPlaybackChanges();
    }

    [Fact]
    public async Task Search_EmptyBookIsNotConfusedWithNoMatchingResults()
    {
        using var session = new PlayerSession();
        await session.LoadAsync();
        session.Model.BookmarkSearchText = "storm";
        Assert.True(session.Model.HasNoBookmarks);
        Assert.False(session.Model.HasNoMatchingBookmarks);
        Assert.False(session.Model.HasMatchingBookmarks);
        session.Model.ClearBookmarkSearchCommand.Execute(null);
        Assert.Equal("0 bookmarks", session.Model.BookmarkCountText);
        Assert.Empty(session.Model.FilteredBookmarks);
        session.AssertNoDialogOrPlaybackChanges();
    }

    [Theory]
    [InlineData("Bookmark at", 2)]
    [InlineData("storm", 2)]
    public async Task Search_QuickAddKeepsQueryAndIncludesOnlyMatchingBookmarks(string query, int matchCount)
    {
        using var session = new PlayerSession();
        SeedSearchBookmarks(session);
        await session.LoadAsync();
        session.Model.BookmarkSearchText = query;
        session.Engine.Position = TimeSpan.FromSeconds(65);

        session.Model.QuickBookmarkCommand.Execute(null);

        Assert.Equal(query, session.Model.BookmarkSearchText);
        Assert.Equal(matchCount, session.Model.FilteredBookmarks.Count);
        Assert.Equal("2 of 5 bookmarks", session.Model.BookmarkCountText);
        Assert.Equal(query == "Bookmark at", session.Model.FilteredBookmarks.Any(
            item => item.Bookmark.Position == TimeSpan.FromSeconds(65)));
        Assert.Equal(5, session.Store.GetForFile(session.FilePath).Count);
        session.AssertNoDialogOrPlaybackChanges();
    }

    [Fact]
    public async Task Search_EditorAddEditAndDeleteReapplyFilterWithoutClearingIt()
    {
        using var session = new PlayerSession();
        SeedSearchBookmarks(session);
        await session.LoadAsync();
        session.Model.BookmarkSearchText = "find me";
        Assert.True(session.Model.HasNoMatchingBookmarks);
        session.Editor.Result = new BookmarkEditResult("New bookmark", "Please FIND ME here");
        await session.Model.AddBookmarkCommand.ExecuteAsync(null);
        var newlyAdded = Assert.Single(session.Model.FilteredBookmarks);
        Assert.Equal("1 of 5 bookmarks", session.Model.BookmarkCountText);

        session.Editor.Result = new BookmarkEditResult("Renamed", "No longer a match");
        await newlyAdded.EditCommand.ExecuteAsync(null);
        Assert.Empty(session.Model.FilteredBookmarks);
        Assert.True(session.Model.HasNoMatchingBookmarks);
        Assert.Equal("find me", session.Model.BookmarkSearchText);
        Assert.Equal(5, session.Model.Bookmarks.Count);

        session.Model.BookmarkSearchText = "renamed";
        Assert.Single(session.Model.FilteredBookmarks).DeleteCommand.Execute(null);
        Assert.Empty(session.Model.FilteredBookmarks);
        Assert.Equal("renamed", session.Model.BookmarkSearchText);
        Assert.Equal("0 of 4 bookmarks", session.Model.BookmarkCountText);
        Assert.Equal(4, session.Store.GetForFile(session.FilePath).Count);
        Assert.Equal(0, session.Engine.PlaybackChanges);
    }

    [Fact]
    public async Task Search_DeleteLastMatchHandlesSingularAndEmptyBookCounts()
    {
        using var session = new PlayerSession();
        await session.LoadAsync();
        session.Model.QuickBookmarkCommand.Execute(null);
        session.Model.BookmarkSearchText = "Bookmark at";
        Assert.Equal("1 of 1 bookmark", session.Model.BookmarkCountText);

        Assert.Single(session.Model.FilteredBookmarks).DeleteCommand.Execute(null);

        Assert.True(session.Model.HasNoBookmarks);
        Assert.False(session.Model.HasNoMatchingBookmarks);
        Assert.False(session.Model.HasMatchingBookmarks);
        Assert.Equal("Bookmark at", session.Model.BookmarkSearchText);
        Assert.Empty(session.Store.GetForFile(session.FilePath));
        session.AssertNoDialogOrPlaybackChanges();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Search_FilteredJumpUsesSelectedBookmarkWithoutChangingPlayState(bool playing)
    {
        using var session = new PlayerSession();
        SeedSearchBookmarks(session);
        await session.LoadAsync();
        session.Model.IsPlaying = playing;
        session.Model.BookmarkSearchText = "warning";

        Assert.Single(session.Model.FilteredBookmarks).JumpCommand.Execute(null);

        Assert.Equal(TimeSpan.FromSeconds(90), session.Engine.Position);
        Assert.Equal(1, session.Engine.PlaybackChanges); // One seek, no play/pause.
        Assert.Equal(playing, session.Model.IsPlaying);
        Assert.Equal("warning", session.Model.BookmarkSearchText);
        Assert.Equal(4, session.Store.GetForFile(session.FilePath).Count);
        Assert.Equal(0, session.Editor.ShowCount);
    }

    [Fact]
    public async Task Search_NavigationKeepsQueryButLoadingAnotherBookResetsItAndItsResults()
    {
        using var session = new PlayerSession();
        SeedSearchBookmarks(session);
        await session.LoadAsync();
        session.Model.BookmarkSearchText = "storm";
        session.Model.SelectedSection = AppSection.Settings;
        session.Model.ShowLibraryCommand.Execute(null);
        session.Model.SelectedSection = AppSection.Player;
        Assert.Equal("storm", session.Model.BookmarkSearchText);
        Assert.Equal(2, session.Model.FilteredBookmarks.Count);
        session.AssertNoDialogOrPlaybackChanges();

        var second = session.Library.Import(session.Workspace.CreateSourceFile("Second.mp3", [2])).Book;
        var now = DateTimeOffset.UtcNow;
        session.Store.Save(new PlaybackBookmark(Guid.NewGuid(), second.FilePath, TimeSpan.Zero,
            "Unrelated", null, null, null, now, now));
        session.Model.ShowLibraryCommand.Execute(null);
        await session.Model.LibraryBooks.Single(item => item.Book.Id == second.Id).PlayCommand.ExecuteAsync(null);

        Assert.Empty(session.Model.BookmarkSearchText);
        Assert.False(session.Model.HasBookmarkSearchText);
        Assert.Equal("1 bookmark", session.Model.BookmarkCountText);
        Assert.Equal(second.FilePath, Assert.Single(session.Model.FilteredBookmarks).Bookmark.FilePath);
        Assert.Equal(4, session.Store.GetForFile(session.FilePath).Count);
    }

    [Fact]
    public async Task Search_UnloadingBookClearsQueryAndFilteredItems()
    {
        using var session = new PlayerSession(removalService: new ConfirmTestBookRemoval());
        SeedSearchBookmarks(session);
        await session.LoadAsync();
        session.Model.BookmarkSearchText = "storm";

        // Normal removal of this fixture's temporary managed copy unloads the current book.
        await Assert.Single(session.Model.LibraryBooks).RemoveBookCommand.ExecuteAsync(null);

        Assert.False(session.Model.IsFileLoaded);
        Assert.Empty(session.Model.BookmarkSearchText);
        Assert.Empty(session.Model.Bookmarks);
        Assert.Empty(session.Model.FilteredBookmarks);
        Assert.False(session.Model.HasMatchingBookmarks);
        Assert.False(session.Model.HasNoMatchingBookmarks);
    }

    private static void SeedSearchBookmarks(PlayerSession session)
    {
        Add(90, "Storm warning", null);
        Add(20, "Opening line", "Listen again");
        Add(45, "Arrival", "A thunderstorm outside.");
        Add(90061, null, null);

        void Add(int seconds, string? name, string? note)
        {
            var now = DateTimeOffset.UtcNow;
            session.Store.Save(new PlaybackBookmark(Guid.NewGuid(), session.FilePath,
                TimeSpan.FromSeconds(seconds), name, note, null, null, now, now));
        }
    }

    private sealed class ConfirmTestBookRemoval : IBookRemovalConfirmationService
    {
        public Task<bool> ConfirmRemovalAsync(LibraryBook book) => Task.FromResult(true);
    }
}
