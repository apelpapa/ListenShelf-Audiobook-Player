using System.Xml.Linq;

namespace ListenShelf.Tests;

public sealed class CrossPlatformProjectConfigurationTests
{
    [Fact]
    public void WindowsNativePackageIsConditional()
    {
        var project = XDocument.Load(FindRepositoryFile(
            "src",
            "ListenShelf.Desktop",
            "ListenShelf.Desktop.csproj"));

        var package = Assert.Single(project.Descendants("PackageReference"),
            element => string.Equals(
                element.Attribute("Include")?.Value,
                "VideoLAN.LibVLC.Windows",
                StringComparison.Ordinal));

        Assert.Contains(
            "ListenShelfTargetWindows",
            package.Attribute("Condition")?.Value,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ApplicationManifest")]
    [InlineData("ApplicationIcon")]
    public void WindowsPackagingPropertiesAreConditional(string propertyName)
    {
        var project = XDocument.Load(FindRepositoryFile(
            "src",
            "ListenShelf.Desktop",
            "ListenShelf.Desktop.csproj"));
        var property = Assert.Single(project.Descendants(propertyName));

        Assert.Contains(
            "ListenShelfTargetWindows",
            property.Attribute("Condition")?.Value,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MacBundleTemplateDeclaresExecutableAndIcon()
    {
        var plist = XDocument.Load(FindRepositoryFile(
            "packaging",
            "macos",
            "Info.plist"));
        var values = plist.Descendants("string").Select(element => element.Value).ToList();

        Assert.Contains("ListenShelf", values);
        Assert.Contains("listenshelf.icns", values);
        Assert.Contains("io.github.apelpapa.ListenShelf", values);
    }

    private static string FindRepositoryFile(params string[] relativePathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                new[] { directory.FullName }.Concat(relativePathParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not find repository file {Path.Combine(relativePathParts)}.");
    }
}
