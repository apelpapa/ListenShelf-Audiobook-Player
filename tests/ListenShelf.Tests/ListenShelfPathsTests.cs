using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Tests;

public sealed class ListenShelfPathsTests
{
    [Fact]
    public void Windows_UsesUnchangedLocalApplicationDataLayout()
    {
        var localData = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "windows-local"));
        var paths = ListenShelfPaths.CreateForWindows(localData);

        Assert.Equal(Path.Combine(localData, "ListenShelf"), paths.DataRootPath);
        Assert.Equal(Path.Combine(paths.DataRootPath, "listenshelf.db"), paths.DatabasePath);
        Assert.Equal(Path.Combine(paths.DataRootPath, "Library"), paths.ManagedLibraryPath);
        Assert.Equal(Path.Combine(paths.DataRootPath, "Covers"), paths.CoverCachePath);
        Assert.Equal(Path.Combine(paths.DataRootPath, "Logs"), paths.LogDirectoryPath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("relative/data")]
    public void Windows_RejectsMissingOrRelativeDataRoot(string? localData)
    {
        Assert.Throws<InvalidOperationException>(() => ListenShelfPaths.CreateForWindows(localData));
    }

    [Fact]
    public void ExplicitDatabasePath_RemainsIndependentOfDesktopDefaults()
    {
        using var workspace = new TestWorkspace();
        var mobileStylePath = Path.Combine(workspace.ManagedLibraryPath, "app-private", "listenshelf.db");
        var database = new ListenShelfDatabase(mobileStylePath);
        Assert.Equal(mobileStylePath, database.DatabasePath);
        Assert.Equal(Path.GetDirectoryName(mobileStylePath), database.DataRootPath);
        Assert.Equal(ListenShelfDatabase.CurrentSchemaVersion, database.SchemaVersion);
    }
}
