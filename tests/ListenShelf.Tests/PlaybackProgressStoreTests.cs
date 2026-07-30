using ListenShelf.Application.Progress;
using ListenShelf.Infrastructure.Progress;
using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Tests;

public sealed class PlaybackProgressStoreTests
{
    [Fact]
    public void Save_RoundTripsAndUpdatesAListeningPosition()
    {
        using var workspace = new TestWorkspace();
        var filePath = workspace.CreateSourceFile("Progress Test.m4b", [1]);
        var firstUpdatedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(-5));
        var secondUpdatedAt = firstUpdatedAt.AddMinutes(10);
        var store = new SqlitePlaybackProgressStore(
            new ListenShelfDatabase(workspace.DatabasePath));

        store.Save(new PlaybackProgress(
            filePath,
            TimeSpan.FromMilliseconds(12_345),
            TimeSpan.FromMilliseconds(98_765),
            firstUpdatedAt));
        store.Save(new PlaybackProgress(
            filePath,
            TimeSpan.FromMilliseconds(23_456),
            TimeSpan.FromMilliseconds(98_765),
            secondUpdatedAt));

        var reloadedStore = new SqlitePlaybackProgressStore(
            new ListenShelfDatabase(workspace.DatabasePath));
        var savedProgress = Assert.IsType<PlaybackProgress>(reloadedStore.Get(filePath));

        Assert.Equal(Path.GetFullPath(filePath), savedProgress.FilePath);
        Assert.Equal(TimeSpan.FromMilliseconds(23_456), savedProgress.Position);
        Assert.Equal(TimeSpan.FromMilliseconds(98_765), savedProgress.Duration);
        Assert.Equal(secondUpdatedAt.ToUniversalTime(), savedProgress.UpdatedAtUtc);
    }

    [Fact]
    public void GetMostRecent_ReturnsTheLatestSavedBook()
    {
        using var workspace = new TestWorkspace();
        var olderPath = workspace.CreateSourceFile("Older.m4b", [1]);
        var newerPath = workspace.CreateSourceFile("Newer.m4b", [2]);
        var store = new SqlitePlaybackProgressStore(
            new ListenShelfDatabase(workspace.DatabasePath));
        var timestamp = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);

        store.Save(new PlaybackProgress(
            olderPath,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromHours(1),
            timestamp));
        store.Save(new PlaybackProgress(
            newerPath,
            TimeSpan.FromMinutes(2),
            TimeSpan.FromHours(2),
            timestamp.AddMinutes(1)));

        var mostRecent = Assert.IsType<PlaybackProgress>(store.GetMostRecent());

        Assert.Equal(Path.GetFullPath(newerPath), mostRecent.FilePath);
        Assert.Equal(TimeSpan.FromMinutes(2), mostRecent.Position);
    }
}
