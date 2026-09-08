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
            Architecture.X64);

        Assert.Null(selectedPath);
    }

    [Fact]
    public void Windows_SelectsFlatRuntimeBesideApplication()
    {
        using var workspace = new TestWorkspace();
        var baseDirectory = workspace.ManagedLibraryPath;
        var runtimeDirectory = Path.Combine(baseDirectory, "libvlc");
        Directory.CreateDirectory(runtimeDirectory);
        File.WriteAllBytes(
            Path.Combine(runtimeDirectory, "libvlc.dll"),
            [0x4D, 0x5A]);

        var selectedPath = LibVlcRuntimeLocator.FindBundledRuntimePath(
            baseDirectory,
            Architecture.X64);

        Assert.Equal(runtimeDirectory, selectedPath);
    }

    [Fact]
    public void Windows_PrefersMatchingArchitectureOverFlatRuntime()
    {
        using var workspace = new TestWorkspace();
        var baseDirectory = workspace.ManagedLibraryPath;
        var runtimeDirectory = Path.Combine(baseDirectory, "libvlc", "win-x64");
        Directory.CreateDirectory(runtimeDirectory);
        File.WriteAllBytes(Path.Combine(runtimeDirectory, "libvlc.dll"), [0x4D, 0x5A]);
        File.WriteAllBytes(Path.Combine(baseDirectory, "libvlc", "libvlc.dll"), [0x4D, 0x5A]);

        var selectedPath = LibVlcRuntimeLocator.FindBundledRuntimePath(
            baseDirectory,
            Architecture.X64);

        Assert.Equal(runtimeDirectory, selectedPath);
    }
}
