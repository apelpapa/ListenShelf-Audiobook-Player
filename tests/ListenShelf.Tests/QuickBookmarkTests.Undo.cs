using ListenShelf.Application.Bookmarks;
using ListenShelf.Desktop.Services;
using ListenShelf.Desktop.ViewModels;

namespace ListenShelf.Tests;

public sealed partial class QuickBookmarkTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Undo_RestoresAllFieldsIncludingUnnamedBookmarksWithoutPlaybackChanges(bool playing, bool named)
    {
        using var session = new PlayerSession();
        var created = new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
        var original = new PlaybackBookmark(Guid.NewGuid(), session.FilePath,
            TimeSpan.FromMilliseconds(90061123), named ? "Important discovery" : null,
            "Remember the detail here.", 4, "Chapter five", created, created.AddMinutes(3));
        session.Store.Save(original);
        await session.LoadAsync();
        session.Model.IsPlaying = playing;
        var changed = new HashSet<string?>();
        session.Model.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        Assert.Single(session.Model.Bookmarks).DeleteCommand.Execute(null);

        Assert.Empty(session.Store.GetForFile(session.FilePath));
        Assert.True(session.Model.HasNoBookmarks);
        Assert.True(session.Model.HasDeletedBookmark);
        Assert.True(session.Model.CanUndoBookmarkDeletion);
        Assert.Equal(named ? "Deleted: Important discovery" : "Deleted: Bookmark at 25:01:01",
            session.Model.DeletedBookmarkText);

        session.Model.UndoBookmarkDeletionCommand.Execute(null);

        Assert.Equal(original, Assert.Single(session.Store.GetForFile(session.FilePath)));
        Assert.Equal(original, Assert.Single(session.Model.FilteredBookmarks).Bookmark);
        Assert.False(session.Model.HasDeletedBookmark);
        Assert.False(session.Model.CanUndoBookmarkDeletion);
        Assert.Empty(session.Model.DeletedBookmarkText);
        Assert.Equal("Bookmark restored.", session.Model.ProgressText);
        Assert.Empty(session.Model.ErrorMessage);
        Assert.Equal(playing, session.Model.IsPlaying);
        Assert.Contains(nameof(MainWindowViewModel.HasDeletedBookmark), changed);
        Assert.Contains(nameof(MainWindowViewModel.CanUndoBookmarkDeletion), changed);
        Assert.Contains(nameof(MainWindowViewModel.DeletedBookmarkText), changed);
        session.AssertNoDialogOrPlaybackChanges();
    }

    [Fact]
    public async Task Undo_OnlyRestoresLatestDeletionAndIgnoresRepeatedOldClicks()
    {
        using var session = new PlayerSession();
        SeedSearchBookmarks(session);
        await session.LoadAsync();
        var first = session.Model.Bookmarks[0];
        var second = session.Model.Bookmarks[1];

        first.DeleteCommand.Execute(null);
        second.DeleteCommand.Execute(null);
        first.DeleteCommand.Execute(null); // A queued stale click must not replace the latest snapshot.
        Assert.Equal("Deleted: Arrival", session.Model.DeletedBookmarkText);
        session.Model.UndoBookmarkDeletionCommand.Execute(null);
        session.Model.UndoBookmarkDeletionCommand.Execute(null); // One-level undo, not a duplicate insert.

        var restored = session.Store.GetForFile(session.FilePath);
        Assert.Equal(3, restored.Count);
        Assert.DoesNotContain(restored, bookmark => bookmark.Id == first.Bookmark.Id);
        Assert.Contains(second.Bookmark, restored);
        Assert.Equal([45d, 90d, 90061d], session.Model.FilteredBookmarks.Select(item => item.Bookmark.Position.TotalSeconds));
        Assert.False(session.Model.UndoBookmarkDeletionCommand.CanExecute(null));
        session.AssertNoDialogOrPlaybackChanges();
    }

    [Theory]
    [InlineData("storm", 2)]
    [InlineData("opening", 1)]
    public async Task Undo_PreservesSearchAndReappliesMatchingCounts(string queryAfterDelete, int matches)
    {
        using var session = new PlayerSession();
        SeedSearchBookmarks(session);
        await session.LoadAsync();
        session.Model.BookmarkSearchText = "arrival";
        var original = Assert.Single(session.Model.FilteredBookmarks).Bookmark;
        Assert.Single(session.Model.FilteredBookmarks).DeleteCommand.Execute(null);
        Assert.True(session.Model.HasNoMatchingBookmarks);
        Assert.True(session.Model.HasDeletedBookmark);
        session.Model.BookmarkSearchText = queryAfterDelete;

        session.Model.UndoBookmarkDeletionCommand.Execute(null);

        Assert.Equal(queryAfterDelete, session.Model.BookmarkSearchText);
        Assert.Equal(matches, session.Model.FilteredBookmarks.Count);
        Assert.Equal($"{matches} of 4 bookmarks", session.Model.BookmarkCountText);
        Assert.Contains(original, session.Store.GetForFile(session.FilePath));
        session.AssertNoDialogOrPlaybackChanges();
    }

    [Fact]
    public async Task Undo_AddingAndEditingOtherBookmarksDoesNotDismissIt()
    {
        using var session = new PlayerSession();
        SeedSearchBookmarks(session);
        await session.LoadAsync();
        var deleted = session.Model.Bookmarks[2].Bookmark;
        session.Model.Bookmarks[2].DeleteCommand.Execute(null);
        session.Model.QuickBookmarkCommand.Execute(null);
        session.Editor.Result = new BookmarkEditResult("Updated survivor", "New note");
        await session.Model.Bookmarks.Single(item => item.Bookmark.Position == TimeSpan.FromSeconds(20))
            .EditCommand.ExecuteAsync(null);

        Assert.True(session.Model.HasDeletedBookmark);
        session.Model.UndoBookmarkDeletionCommand.Execute(null);

        var stored = session.Store.GetForFile(session.FilePath);
        Assert.Equal(5, stored.Count);
        Assert.Contains(deleted, stored);
        Assert.Contains(stored, bookmark => bookmark.Name == "Updated survivor" && bookmark.Note == "New note");
        Assert.Equal(0, session.Engine.PlaybackChanges);
    }

    [Fact]
    public async Task Undo_DismissalLeavesBookmarkDeletedAndDisablesUndo()
    {
        using var session = new PlayerSession();
        await session.LoadAsync();
        session.Model.QuickBookmarkCommand.Execute(null);
        Assert.Single(session.Model.Bookmarks).DeleteCommand.Execute(null);

        session.Model.DismissBookmarkUndoCommand.Execute(null);
        session.Model.UndoBookmarkDeletionCommand.Execute(null);

        Assert.False(session.Model.HasDeletedBookmark);
        Assert.False(session.Model.CanUndoBookmarkDeletion);
        Assert.Empty(session.Model.DeletedBookmarkText);
        Assert.Empty(session.Store.GetForFile(session.FilePath));
        session.AssertNoDialogOrPlaybackChanges();
    }

    [Fact]
    public async Task Undo_FailedDeletionKeepsPreviousUndoAndDoesNotRemoveTheNewTarget()
    {
        using var session = new PlayerSession();
        SeedSearchBookmarks(session);
        await session.LoadAsync();
        var first = session.Model.Bookmarks[0].Bookmark;
        session.Model.Bookmarks[0].DeleteCommand.Execute(null);
        ExecuteBookmarkTestSql(session, """
            CREATE TRIGGER reject_bookmark_delete BEFORE DELETE ON playback_bookmarks
            BEGIN SELECT RAISE(FAIL, 'Test delete failure'); END;
            """);

        session.Model.Bookmarks[0].DeleteCommand.Execute(null);

        Assert.Contains("could not be deleted", session.Model.ErrorMessage);
        Assert.Equal("Deleted: Opening line", session.Model.DeletedBookmarkText);
        Assert.Equal(3, session.Store.GetForFile(session.FilePath).Count);
        session.Model.UndoBookmarkDeletionCommand.Execute(null);
        Assert.Equal(4, session.Store.GetForFile(session.FilePath).Count);
        Assert.Contains(first, session.Store.GetForFile(session.FilePath));
        Assert.Empty(session.Model.ErrorMessage);
        session.AssertNoDialogOrPlaybackChanges();
    }

    [Fact]
    public async Task Undo_FailedRestoreRetainsSnapshotForRetry()
    {
        using var session = new PlayerSession();
        await session.LoadAsync();
        session.Model.QuickBookmarkCommand.Execute(null);
        var original = Assert.Single(session.Model.Bookmarks).Bookmark;
        Assert.Single(session.Model.Bookmarks).DeleteCommand.Execute(null);
        ExecuteBookmarkTestSql(session, """
            CREATE TRIGGER reject_bookmark_restore BEFORE INSERT ON playback_bookmarks
            BEGIN SELECT RAISE(FAIL, 'Test restore failure'); END;
            """);

        session.Model.UndoBookmarkDeletionCommand.Execute(null);

        Assert.True(session.Model.HasDeletedBookmark);
        Assert.True(session.Model.UndoBookmarkDeletionCommand.CanExecute(null));
        Assert.Contains("try Undo again", session.Model.ErrorMessage);
        Assert.Empty(session.Store.GetForFile(session.FilePath));
        ExecuteBookmarkTestSql(session, "DROP TRIGGER reject_bookmark_restore;");
        session.Model.UndoBookmarkDeletionCommand.Execute(null);
        Assert.Equal(original, Assert.Single(session.Store.GetForFile(session.FilePath)));
        Assert.False(session.Model.HasDeletedBookmark);
        Assert.Empty(session.Model.ErrorMessage);
        session.AssertNoDialogOrPlaybackChanges();
    }

    [Fact]
    public async Task Undo_DoesNotOverwriteAnAlreadyRestoredAndEditedBookmark()
    {
        using var session = new PlayerSession();
        await session.LoadAsync();
        session.Model.QuickBookmarkCommand.Execute(null);
        var original = Assert.Single(session.Model.Bookmarks).Bookmark;
        Assert.Single(session.Model.Bookmarks).DeleteCommand.Execute(null);
        var newer = original with { Name = "Restored elsewhere", Note = "Keep these edits", UpdatedAtUtc = original.UpdatedAtUtc.AddMinutes(1) };
        session.Store.Save(newer);

        session.Model.UndoBookmarkDeletionCommand.Execute(null);

        Assert.Equal(newer, Assert.Single(session.Store.GetForFile(session.FilePath)));
        Assert.Equal(newer, Assert.Single(session.Model.Bookmarks).Bookmark);
        Assert.Contains("already present", session.Model.ProgressText);
        Assert.False(session.Model.HasDeletedBookmark);
        session.AssertNoDialogOrPlaybackChanges();
    }

    [Fact]
    public async Task Undo_StaleItemDeletionCapturesLatestEditedDetails()
    {
        using var session = new PlayerSession();
        await session.LoadAsync();
        session.Model.QuickBookmarkCommand.Execute(null);
        var staleItem = Assert.Single(session.Model.Bookmarks);
        session.Editor.Result = new BookmarkEditResult("Latest name", "Latest note");
        await staleItem.EditCommand.ExecuteAsync(null);
        var latest = Assert.Single(session.Model.Bookmarks).Bookmark;

        staleItem.DeleteCommand.Execute(null);
        session.Model.UndoBookmarkDeletionCommand.Execute(null);

        Assert.Equal(latest, Assert.Single(session.Store.GetForFile(session.FilePath)));
        Assert.Equal(0, session.Engine.PlaybackChanges);
    }

    [Fact]
    public async Task Undo_AvailabilityFollowsLoadedBusyAndDisposedState()
    {
        using var session = new PlayerSession();
        var notifications = 0;
        session.Model.UndoBookmarkDeletionCommand.CanExecuteChanged += (_, _) => notifications++;
        AssertUnavailable();
        await session.LoadAsync();
        AssertUnavailable();
        session.Model.QuickBookmarkCommand.Execute(null);
        var item = Assert.Single(session.Model.Bookmarks);
        session.Model.IsBusy = true;
        item.DeleteCommand.Execute(null);
        Assert.False(session.Model.HasDeletedBookmark);
        Assert.Single(session.Model.Bookmarks);
        session.Model.IsBusy = false;
        item.DeleteCommand.Execute(null);
        Assert.True(session.Model.UndoBookmarkDeletionCommand.CanExecute(null));

        session.Model.IsBusy = true;
        AssertUnavailable();
        Assert.True(session.Model.HasDeletedBookmark);
        session.Model.IsBusy = false;
        Assert.True(session.Model.UndoBookmarkDeletionCommand.CanExecute(null));
        session.Model.IsFileLoaded = false;
        AssertUnavailable();
        session.Model.IsFileLoaded = true;
        session.Model.Dispose();
        AssertUnavailable();
        item.DeleteCommand.Execute(null);

        Assert.False(session.Model.HasDeletedBookmark);
        Assert.Empty(session.Store.GetForFile(session.FilePath));
        Assert.True(notifications >= 8);
        session.AssertNoDialogOrPlaybackChanges();

        void AssertUnavailable()
        {
            Assert.False(session.Model.CanUndoBookmarkDeletion);
            Assert.False(session.Model.UndoBookmarkDeletionCommand.CanExecute(null));
            session.Model.UndoBookmarkDeletionCommand.Execute(null);
        }
    }

    [Fact]
    public async Task Undo_BookSwitchClearsSnapshotAndRejectsOldBookCallbacks()
    {
        using var session = new PlayerSession();
        SeedSearchBookmarks(session);
        await session.LoadAsync();
        session.Model.Bookmarks[0].DeleteCommand.Execute(null);
        var oldItem = session.Model.Bookmarks[0];
        session.Model.SelectedSection = AppSection.Settings;
        session.Model.ShowLibraryCommand.Execute(null);
        Assert.True(session.Model.HasDeletedBookmark); // Section navigation is not a book change.
        var second = session.Library.Import(session.Workspace.CreateSourceFile("Second.mp3", [2])).Book;
        session.Model.ShowLibraryCommand.Execute(null);

        await session.Model.LibraryBooks.Single(item => item.Book.Id == second.Id).PlayCommand.ExecuteAsync(null);
        session.Engine.PlaybackChanges = 0;
        oldItem.DeleteCommand.Execute(null);
        session.Model.UndoBookmarkDeletionCommand.Execute(null);

        Assert.False(session.Model.HasDeletedBookmark);
        Assert.False(session.Model.UndoBookmarkDeletionCommand.CanExecute(null));
        Assert.Empty(session.Model.Bookmarks);
        Assert.Equal(3, session.Store.GetForFile(session.FilePath).Count);
        Assert.Empty(session.Store.GetForFile(second.FilePath));
        session.AssertNoDialogOrPlaybackChanges();
    }

    [Fact]
    public async Task Undo_RemovingBookCannotResurrectOrphanedBookmarkData()
    {
        using var session = new PlayerSession(removalService: new ConfirmTestBookRemoval());
        await session.LoadAsync();
        session.Model.QuickBookmarkCommand.Execute(null);
        Assert.Single(session.Model.Bookmarks).DeleteCommand.Execute(null);

        await Assert.Single(session.Model.LibraryBooks).RemoveBookCommand.ExecuteAsync(null);
        session.Model.UndoBookmarkDeletionCommand.Execute(null);

        Assert.False(session.Model.IsFileLoaded);
        Assert.False(session.Model.HasDeletedBookmark);
        Assert.False(session.Model.CanUndoBookmarkDeletion);
        Assert.Empty(session.Library.GetBooks());
        Assert.Empty(session.Store.GetForFile(session.FilePath));
    }

    private static void ExecuteBookmarkTestSql(PlayerSession session, string sql)
    {
        using var connection = session.Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
