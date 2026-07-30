using ListenShelf.Application.Bookmarks;
using ListenShelf.Infrastructure.Bookmarks;
using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Tests;

public sealed class PlaybackBookmarkStoreTests
{
    [Fact]
    public void SaveUpdateOrderAndDelete_PersistTheBookmarkCollection()
    {
        using var workspace = new TestWorkspace();
        var filePath = workspace.CreateSourceFile("Bookmarks.m4b", [1]);
        var store = new SqlitePlaybackBookmarkStore(
            new ListenShelfDatabase(workspace.DatabasePath));
        var createdAt = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
        var first = CreateBookmark(filePath, TimeSpan.FromSeconds(90), createdAt);
        var second = CreateBookmark(filePath, TimeSpan.FromSeconds(10), createdAt.AddSeconds(1));
        var third = CreateBookmark(filePath, TimeSpan.FromSeconds(50), createdAt.AddSeconds(2));

        store.Save(first);
        store.Save(second);
        store.Save(third);

        Assert.Equal(
            [10d, 50d, 90d],
            store.GetForFile(filePath)
                .Select(bookmark => bookmark.Position.TotalSeconds)
                .ToArray());

        var updatedAt = createdAt.AddMinutes(5);
        store.Save(third with
        {
            Position = TimeSpan.FromSeconds(5),
            Name = "  Important part  ",
            Note = "  Remember this  ",
            ChapterIndex = 3,
            ChapterTitle = "  Chapter Four  ",
            UpdatedAtUtc = updatedAt,
        });

        var reloadedStore = new SqlitePlaybackBookmarkStore(
            new ListenShelfDatabase(workspace.DatabasePath));
        var updated = reloadedStore
            .GetForFile(filePath)
            .Single(bookmark => bookmark.Id == third.Id);

        Assert.Equal(TimeSpan.FromSeconds(5), updated.Position);
        Assert.Equal("Important part", updated.Name);
        Assert.Equal("Remember this", updated.Note);
        Assert.Equal(3, updated.ChapterIndex);
        Assert.Equal("Chapter Four", updated.ChapterTitle);
        Assert.Equal(third.CreatedAtUtc, updated.CreatedAtUtc);
        Assert.Equal(updatedAt, updated.UpdatedAtUtc);

        reloadedStore.Delete(second.Id);

        var remaining = reloadedStore.GetForFile(filePath);
        Assert.DoesNotContain(remaining, bookmark => bookmark.Id == second.Id);
        Assert.Equal(2, remaining.Count);
    }

    [Fact]
    public void Save_RejectsInvalidBookmarkValues()
    {
        using var workspace = new TestWorkspace();
        var filePath = workspace.CreateSourceFile("Invalid Bookmark.m4b", [1]);
        var store = new SqlitePlaybackBookmarkStore(
            new ListenShelfDatabase(workspace.DatabasePath));
        var timestamp = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentException>(() => store.Save(new PlaybackBookmark(
            Guid.Empty,
            filePath,
            TimeSpan.Zero,
            null,
            null,
            null,
            null,
            timestamp,
            timestamp)));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.Save(new PlaybackBookmark(
            Guid.NewGuid(),
            filePath,
            TimeSpan.FromSeconds(-1),
            null,
            null,
            null,
            null,
            timestamp,
            timestamp)));
    }

    private static PlaybackBookmark CreateBookmark(
        string filePath,
        TimeSpan position,
        DateTimeOffset createdAt) =>
        new(
            Guid.NewGuid(),
            filePath,
            position,
            null,
            null,
            null,
            null,
            createdAt,
            createdAt);
}
