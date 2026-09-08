using System.Xml.Linq;

namespace ListenShelf.Tests;

public sealed class WindowsProjectConfigurationTests
{
    [Fact]
    public void DesktopUsesWindowsBackendAndBundledWindowsPlaybackRuntime()
    {
        var project = XDocument.Load(FindRepositoryFile("src/ListenShelf.Desktop/ListenShelf.Desktop.csproj"));
        var packages = project.Descendants("PackageReference").ToDictionary(e => e.Attribute("Include")!.Value);
        Assert.Contains("Avalonia.Win32", packages.Keys);
        Assert.Contains("Avalonia.Skia", packages.Keys);
        Assert.Contains("Avalonia.HarfBuzz", packages.Keys);
        Assert.DoesNotContain("Avalonia.Desktop", packages.Keys);
        Assert.DoesNotContain("Avalonia.Native", packages.Keys);
        Assert.DoesNotContain("Avalonia.X11", packages.Keys);
        Assert.Null(packages["VideoLAN.LibVLC.Windows"].Attribute("Condition"));
        Assert.Equal("win-x64", Assert.Single(project.Descendants("RuntimeIdentifier")).Value);
        Assert.Single(project.Descendants("Target"), e => (string?)e.Attribute("Name") == "ValidateWindowsRuntimeIdentifier");

        var program = File.ReadAllText(FindRepositoryFile("src/ListenShelf.Desktop/Program.cs"));
        Assert.Contains(".UseWin32()", program);
        Assert.Contains(".UseSkia()", program);
        Assert.Contains(".UseHarfBuzz()", program);
        Assert.DoesNotContain(".UsePlatformDetect()", program);
        Assert.Contains("--verify-native-runtime", program);
    }

    [Theory]
    [InlineData("ApplicationManifest", "app.manifest")]
    [InlineData("ApplicationIcon", "Assets\\Branding\\listenshelf.ico")]
    public void DesktopAlwaysIncludesWindowsBranding(string name, string expected)
    {
        var project = XDocument.Load(FindRepositoryFile("src/ListenShelf.Desktop/ListenShelf.Desktop.csproj"));
        var property = Assert.Single(project.Descendants(name));
        Assert.Equal(expected, property.Value);
        Assert.Null(property.Attribute("Condition"));
    }

    [Fact]
    public void BuildWorkflowRemainsManualAndWindowsOnly()
    {
        var workflow = File.ReadAllText(FindRepositoryFile(".github/workflows/build-and-test.yml"));
        Assert.Contains("workflow_dispatch:", workflow);
        Assert.Contains("runs-on: windows-latest", workflow);
        Assert.DoesNotContain("matrix.", workflow);
        Assert.DoesNotContain("ubuntu-", workflow);
        Assert.DoesNotContain("macos-", workflow);
        Assert.DoesNotContain("\n  push:", workflow);
        Assert.DoesNotContain("\n  pull_request:", workflow);
    }

    [Fact]
    public void WindowsPackagingPointsToExistingWindowsTestingGuide()
    {
        var script = File.ReadAllText(FindRepositoryFile("build/Publish-WindowsRelease.ps1"));
        Assert.Contains("docs\\WINDOWS_TESTING.md", script);
        Assert.True(File.Exists(FindRepositoryFile("docs/WINDOWS_TESTING.md")));
        Assert.Contains("'--runtime', 'win-x64'", script);
        Assert.Contains("Test-WindowsInstallerDataSafety.ps1", script);
    }

    [Fact]
    public void AndroidRemainsSeparateWithItsOwnRuntimeAndSharedLibraries()
    {
        var desktopSolution = XDocument.Load(FindRepositoryFile("ListenShelf.slnx"));
        Assert.DoesNotContain(desktopSolution.Descendants("Project"), e => e.Attribute("Path")!.Value.Contains("ListenShelf.Android"));
        var androidSolution = XDocument.Load(FindRepositoryFile("ListenShelf.Android.slnx"));
        Assert.Contains(androidSolution.Descendants("Project"), e => e.Attribute("Path")!.Value.Contains("ListenShelf.Android"));
        Assert.DoesNotContain(androidSolution.Descendants("Project"), e => e.Attribute("Path")!.Value.Contains("ListenShelf.Desktop"));

        var androidProject = XDocument.Load(FindRepositoryFile("src/ListenShelf.Android/ListenShelf.Android.csproj"));
        Assert.Equal("net10.0-android", Assert.Single(androidProject.Descendants("TargetFramework")).Value);
        Assert.Contains(androidProject.Descendants("PackageReference"), e => (string?)e.Attribute("Include") == "VideoLAN.LibVLC.Android");
        Assert.DoesNotContain(androidProject.Descendants("PackageReference"), e => (string?)e.Attribute("Include") == "VideoLAN.LibVLC.Windows");
        var mobileSession = File.ReadAllText(FindRepositoryFile("src/ListenShelf.Android/MobileSession.cs"));
        Assert.Contains("new LibVlcAudioEngine(() => LibVLCSharp.Shared.Core.Initialize())", mobileSession);
        Assert.Contains("_context.FilesDir!.CanonicalPath", mobileSession);
        Assert.True(File.Exists(FindRepositoryFile("src/ListenShelf.Desktop/Assets/Branding/listenshelf-1024.png")));
    }

    private static string FindRepositoryFile(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"Could not find repository file {relativePath}.");
    }
}
