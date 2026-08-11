using ListenShelf.Application.Settings;
using ListenShelf.Infrastructure.Settings;
using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Tests;

public sealed class AppSettingsStoreTests
{
    [Fact]
    public void NewStore_UsesStableDefaults()
    {
        using var workspace = new TestWorkspace();
        var store = new SqliteAppSettingsStore(
            new ListenShelfDatabase(workspace.DatabasePath));

        Assert.Equal(AppTheme.Dark, store.GetTheme());
        Assert.Equal(LibraryViewMode.List, store.GetLibraryViewMode());
        Assert.Equal(LibraryGroupMode.None, store.GetLibraryGroupMode());
        Assert.Equal(LibrarySortMode.Title, store.GetLibrarySortMode());
        Assert.Equal(LibraryStatusFilter.All, store.GetLibraryStatusFilter());
        Assert.Equal(220d, store.GetLibraryTileWidth());
        Assert.Equal(80d, store.GetPlaybackVolume());
        Assert.Equal(1d, store.GetPlaybackRate());
        Assert.Equal(15, store.GetRewindSeconds());
        Assert.Equal(30, store.GetForwardSeconds());
    }

    [Fact]
    public void SavedSettings_RoundTripAcrossStoreInstances()
    {
        using var workspace = new TestWorkspace();
        var store = new SqliteAppSettingsStore(
            new ListenShelfDatabase(workspace.DatabasePath));

        store.SaveTheme(AppTheme.Light);
        store.SaveLibraryViewMode(LibraryViewMode.Tiles);
        store.SaveLibraryGroupMode(LibraryGroupMode.Author);
        store.SaveLibrarySortMode(LibrarySortMode.RecentlyPlayed);
        store.SaveLibraryStatusFilter(LibraryStatusFilter.InProgress);
        store.SaveLibraryTileWidth(275d);
        store.SavePlaybackVolume(64d);
        store.SavePlaybackRate(1.5d);
        store.SaveRewindSeconds(10);
        store.SaveForwardSeconds(45);

        var reloadedStore = new SqliteAppSettingsStore(
            new ListenShelfDatabase(workspace.DatabasePath));

        Assert.Equal(AppTheme.Light, reloadedStore.GetTheme());
        Assert.Equal(LibraryViewMode.Tiles, reloadedStore.GetLibraryViewMode());
        Assert.Equal(LibraryGroupMode.Author, reloadedStore.GetLibraryGroupMode());
        Assert.Equal(LibrarySortMode.RecentlyPlayed, reloadedStore.GetLibrarySortMode());
        Assert.Equal(LibraryStatusFilter.InProgress, reloadedStore.GetLibraryStatusFilter());
        Assert.Equal(275d, reloadedStore.GetLibraryTileWidth());
        Assert.Equal(64d, reloadedStore.GetPlaybackVolume());
        Assert.Equal(1.5d, reloadedStore.GetPlaybackRate());
        Assert.Equal(10, reloadedStore.GetRewindSeconds());
        Assert.Equal(45, reloadedStore.GetForwardSeconds());
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("999")]
    [InlineData("")]
    public void LibraryBrowsing_UsesDefaultsForInvalidStoredValues(string value)
    {
        using var workspace = new TestWorkspace();
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO app_settings (setting_key, setting_value)
            VALUES ('library.sort_mode', $value), ('library.status_filter', $value);
            """;
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();

        var store = new SqliteAppSettingsStore(database);
        Assert.Equal(LibrarySortMode.Title, store.GetLibrarySortMode());
        Assert.Equal(LibraryStatusFilter.All, store.GetLibraryStatusFilter());
        Assert.Throws<ArgumentOutOfRangeException>(() => store.SaveLibrarySortMode((LibrarySortMode)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.SaveLibraryStatusFilter((LibraryStatusFilter)999));
    }

    [Fact]
    public void LibraryBrowsing_AllChoicesRoundTrip()
    {
        using var workspace = new TestWorkspace();
        var store = new SqliteAppSettingsStore(new ListenShelfDatabase(workspace.DatabasePath));
        foreach (var mode in Enum.GetValues<LibrarySortMode>())
        {
            store.SaveLibrarySortMode(mode);
            Assert.Equal(mode, store.GetLibrarySortMode());
        }

        foreach (var status in Enum.GetValues<LibraryStatusFilter>())
        {
            store.SaveLibraryStatusFilter(status);
            Assert.Equal(status, store.GetLibraryStatusFilter());
        }
    }

    [Theory]
    [InlineData(-1d)]
    [InlineData(101d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void SavePlaybackVolume_RejectsInvalidValues(double value)
    {
        using var workspace = new TestWorkspace();
        var store = new SqliteAppSettingsStore(
            new ListenShelfDatabase(workspace.DatabasePath));

        Assert.Throws<ArgumentOutOfRangeException>(() => store.SavePlaybackVolume(value));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(600)]
    public void SkipIntervals_AcceptRangeBoundaries(int seconds)
    {
        using var workspace = new TestWorkspace();
        var store = new SqliteAppSettingsStore(new ListenShelfDatabase(workspace.DatabasePath));

        store.SaveRewindSeconds(seconds);
        store.SaveForwardSeconds(seconds);

        Assert.Equal(seconds, store.GetRewindSeconds());
        Assert.Equal(seconds, store.GetForwardSeconds());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(601)]
    [InlineData(int.MaxValue)]
    public void SkipIntervals_RejectInvalidValuesWithoutOverwritingSavedSettings(int seconds)
    {
        using var workspace = new TestWorkspace();
        var store = new SqliteAppSettingsStore(new ListenShelfDatabase(workspace.DatabasePath));
        store.SaveRewindSeconds(20);
        store.SaveForwardSeconds(40);

        Assert.Throws<ArgumentOutOfRangeException>(() => store.SaveRewindSeconds(seconds));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.SaveForwardSeconds(seconds));

        Assert.Equal(20, store.GetRewindSeconds());
        Assert.Equal(40, store.GetForwardSeconds());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a number")]
    [InlineData("0")]
    [InlineData("-15")]
    [InlineData("601")]
    [InlineData("2147483648")]
    [InlineData("15.5")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void SkipIntervals_UseDefaultsForMalformedStoredValues(string value)
    {
        using var workspace = new TestWorkspace();
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                INSERT INTO app_settings (setting_key, setting_value)
                VALUES ('player.rewind_seconds', $value), ('player.forward_seconds', $value);
                """;
            command.Parameters.AddWithValue("$value", value);
            command.ExecuteNonQuery();
        }

        var store = new SqliteAppSettingsStore(database);
        Assert.Equal(15, store.GetRewindSeconds());
        Assert.Equal(30, store.GetForwardSeconds());
    }
}
