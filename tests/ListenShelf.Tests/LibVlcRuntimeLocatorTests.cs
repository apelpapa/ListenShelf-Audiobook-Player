using System.Runtime.InteropServices;
using ListenShelf.Playback.LibVlc;

namespace ListenShelf.Tests;

public sealed class LibVlcRuntimeLocatorTests
{
    [Theory]
    [InlineData(Architecture.X64, "win-x64")]
    [InlineData(Architecture.X86, "win-x86")]
    [InlineData(Architecture.Arm64, "win-arm64")]
    public void Windows_SelectsArchitectureDirectoryContainingNativeLibrary(
        Architecture architecture,
        string expectedRuntimeFolder)
    {
        using var workspace = new TestWorkspace();
        var baseDirectory = workspace.ManagedLibraryPath;
        var architectureDirectory = Path.Combine(
            baseDirectory,
            "libvlc",
            expectedRuntimeFolder);
        Directory.CreateDirectory(architectureDirectory);
        File.WriteAllBytes(
            Path.Combine(architectureDirectory, "libvlc.dll"),
            [0x4D, 0x5A]);

        var selectedPath = LibVlcRuntimeLocator.FindBundledRuntimePath(
            baseDirectory,
            DesktopRuntimePlatform.Windows,
            architecture);

        Assert.Equal(architectureDirectory, selectedPath);
    }

    [Fact]
    public void Windows_DoesNotSelectParentThatContainsOnlyArchitectureFolders()
    {
        using var workspace = new TestWorkspace();
        var baseDirectory = workspace.ManagedLibraryPath;
        Directory.CreateDirectory(Path.Combine(baseDirectory, "libvlc", "win-x86"));
        File.WriteAllBytes(
            Path.Combine(baseDirectory, "libvlc", "win-x86", "libvlc.dll"),
            [0x4D, 0x5A]);

        var selectedPath = LibVlcRuntimeLocator.FindBundledRuntimePath(
            baseDirectory,
            DesktopRuntimePlatform.Windows,
            Architecture.X64);

        Assert.Null(selectedPath);
    }

    [Fact]
    public void Linux_SelectsPrivateRuntimeBesideApplication()
    {
        using var workspace = new TestWorkspace();
        var baseDirectory = workspace.ManagedLibraryPath;
        var runtimeDirectory = Path.Combine(baseDirectory, "libvlc");
        Directory.CreateDirectory(runtimeDirectory);
        File.WriteAllBytes(
            Path.Combine(runtimeDirectory, "libvlc.so.5"),
            [0x7F, 0x45, 0x4C, 0x46]);

        var selectedPath = LibVlcRuntimeLocator.FindBundledRuntimePath(
            baseDirectory,
            DesktopRuntimePlatform.Linux,
            Architecture.X64);

        Assert.Equal(runtimeDirectory, selectedPath);
    }

    [Fact]
    public void MacOS_SelectsPrivateRuntimeInsideAppFrameworks()
    {
        using var workspace = new TestWorkspace();
        var contentsDirectory = Path.Combine(
            workspace.ManagedLibraryPath,
            "ListenShelf.app",
            "Contents");
        var baseDirectory = Path.Combine(contentsDirectory, "MacOS");
        var runtimeDirectory = Path.Combine(
            contentsDirectory,
            "Frameworks",
            "libvlc",
            "lib");
        Directory.CreateDirectory(baseDirectory);
        Directory.CreateDirectory(runtimeDirectory);
        File.WriteAllBytes(
            Path.Combine(runtimeDirectory, "libvlc.dylib"),
            [0xCF, 0xFA, 0xED, 0xFE]);

        var selectedPath = LibVlcRuntimeLocator.FindBundledRuntimePath(
            baseDirectory,
            DesktopRuntimePlatform.MacOS,
            Architecture.Arm64);

        Assert.Equal(runtimeDirectory, selectedPath);
    }
}
