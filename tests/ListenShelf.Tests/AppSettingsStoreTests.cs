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
        Assert.Equal(220d, store.GetLibraryTileWidth());
        Assert.Equal(80d, store.GetPlaybackVolume());
        Assert.Equal(1d, store.GetPlaybackRate());
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
        store.SaveLibraryTileWidth(275d);
        store.SavePlaybackVolume(64d);
        store.SavePlaybackRate(1.5d);

        var reloadedStore = new SqliteAppSettingsStore(
            new ListenShelfDatabase(workspace.DatabasePath));

        Assert.Equal(AppTheme.Light, reloadedStore.GetTheme());
        Assert.Equal(LibraryViewMode.Tiles, reloadedStore.GetLibraryViewMode());
        Assert.Equal(LibraryGroupMode.Author, reloadedStore.GetLibraryGroupMode());
        Assert.Equal(275d, reloadedStore.GetLibraryTileWidth());
        Assert.Equal(64d, reloadedStore.GetPlaybackVolume());
        Assert.Equal(1.5d, reloadedStore.GetPlaybackRate());
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
}
