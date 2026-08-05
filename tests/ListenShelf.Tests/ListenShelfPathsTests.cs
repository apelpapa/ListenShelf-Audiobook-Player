using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Tests;

public sealed class ListenShelfPathsTests
{
    [Fact]
    public void Windows_UsesLocalApplicationData()
    {
        var localData = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "windows-local"));

        var paths = ListenShelfPaths.CreateForPlatform(
            DesktopPlatformKind.Windows,
            localData,
            userProfilePath: null);

        Assert.Equal(Path.Combine(localData, "ListenShelf"), paths.DataRootPath);
        Assert.Equal(Path.Combine(paths.DataRootPath, "listenshelf.db"), paths.DatabasePath);
    }

    [Fact]
    public void MacOS_UsesApplicationSupport()
    {
        var userProfile = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mac-user"));

        var paths = ListenShelfPaths.CreateForPlatform(
            DesktopPlatformKind.MacOS,
            localApplicationDataPath: null,
            userProfile);

        Assert.Equal(
            Path.Combine(userProfile, "Library", "Application Support", "ListenShelf"),
            paths.DataRootPath);
    }

    [Fact]
    public void Linux_UsesAbsoluteXdgDataHomeWhenProvided()
    {
        var xdgDataHome = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "xdg-data"));

        var paths = ListenShelfPaths.CreateForPlatform(
            DesktopPlatformKind.Linux,
            localApplicationDataPath: null,
            userProfilePath: null,
            xdgDataHome);

        Assert.Equal(Path.Combine(xdgDataHome, "ListenShelf"), paths.DataRootPath);
    }

    [Fact]
    public void Linux_FallsBackToUserLocalShareForRelativeXdgValue()
    {
        var userProfile = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "linux-user"));

        var paths = ListenShelfPaths.CreateForPlatform(
            DesktopPlatformKind.Linux,
            localApplicationDataPath: null,
            userProfile,
            xdgDataHome: "relative/path");

        Assert.Equal(
            Path.Combine(userProfile, ".local", "share", "ListenShelf"),
            paths.DataRootPath);
    }
}
