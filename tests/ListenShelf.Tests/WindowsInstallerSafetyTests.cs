using System.Xml.Linq;

namespace ListenShelf.Tests;

public sealed class WindowsInstallerSafetyTests
{
    private const string ExpectedUpgradeCode = "{0C87279E-1529-4254-A0A9-111496D4E2ED}";

    [Fact]
    public void Installer_InstallsApplicationFilesUnderProgramFilesAndRetainsUpgradeIdentity()
    {
        var document = LoadInstallerSource();
        var wixNamespace = document.Root!.Name.Namespace;
        var package = Assert.Single(document.Root.Elements(wixNamespace + "Package"));

        Assert.Equal(ExpectedUpgradeCode, (string?)package.Attribute("UpgradeCode"));
        Assert.NotNull(package.Element(wixNamespace + "MajorUpgrade"));

        var programFilesDirectory = Assert.Single(
            package.Elements(wixNamespace + "StandardDirectory"),
            element => (string?)element.Attribute("Id") == "ProgramFiles64Folder");
        Assert.Contains(
            programFilesDirectory.Elements(wixNamespace + "Directory"),
            element => (string?)element.Attribute("Id") == "INSTALLFOLDER");
    }

    [Fact]
    public void Installer_DoesNotClaimOrDeletePerUserData()
    {
        var document = LoadInstallerSource();
        var elements = document.Descendants().ToArray();
        var protectedDirectoryIds = new[]
        {
            "LocalAppDataFolder",
            "AppDataFolder",
            "CommonAppDataFolder",
        };

        foreach (var directoryId in protectedDirectoryIds)
        {
            Assert.DoesNotContain(
                elements,
                element =>
                    (string?)element.Attribute("Id") == directoryId ||
                    (string?)element.Attribute("Directory") == directoryId);
        }

        var destructiveElementNames = new[] { "RemoveFile", "RemoveFolder", "RemoveFolderEx" };
        Assert.DoesNotContain(
            elements,
            element => destructiveElementNames.Contains(element.Name.LocalName, StringComparer.Ordinal));
    }

    private static XDocument LoadInstallerSource() =>
        XDocument.Load(FindRepositoryFile("packaging", "windows", "ListenShelf.wxs"));

    private static string FindRepositoryFile(params string[] relativePathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var solutionPath = Path.Combine(directory.FullName, "ListenShelf.slnx");
            if (File.Exists(solutionPath))
            {
                return Path.Combine(new[] { directory.FullName }.Concat(relativePathParts).ToArray());
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the ListenShelf repository root.");
    }
}
