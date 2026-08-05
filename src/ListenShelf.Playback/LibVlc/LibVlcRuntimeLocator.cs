using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

namespace ListenShelf.Playback.LibVlc;

public static class LibVlcRuntimeLocator
{
    public const string CustomRuntimePathEnvironmentVariable =
        "LISTENSHELF_LIBVLC_PATH";

    public static string RuntimeDescription =>
        $"{GetPlatformName()} {RuntimeInformation.ProcessArchitecture} • .NET {Environment.Version}";

    public static void Initialize()
    {
        var customPath = Environment.GetEnvironmentVariable(
            CustomRuntimePathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            var normalizedCustomPath = Path.GetFullPath(customPath);
            if (!Directory.Exists(normalizedCustomPath))
            {
                throw CreateException(
                    $"The custom LibVLC directory does not exist: {normalizedCustomPath}",
                    new DirectoryNotFoundException(normalizedCustomPath));
            }

            InitializeFromPath(normalizedCustomPath);
            return;
        }

        var bundledPath = FindBundledRuntimePath(
            AppContext.BaseDirectory,
            GetPlatform(),
            RuntimeInformation.ProcessArchitecture);
        if (bundledPath is not null)
        {
            InitializeFromPath(bundledPath);
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            const string installedVlcPath = "/Applications/VLC.app/Contents/MacOS/lib";
            if (Directory.Exists(installedVlcPath))
            {
                InitializeFromPath(installedVlcPath);
                return;
            }
        }

        try
        {
            Core.Initialize();
        }
        catch (Exception exception) when (IsNativeRuntimeException(exception))
        {
            throw CreateException(
                "ListenShelf could not locate or load a compatible LibVLC runtime.",
                exception);
        }
    }

    public static string GetPlatformHelp() => OperatingSystem.IsWindows()
        ? "The Windows build should contain its own LibVLC runtime. Reinstall or extract the complete ListenShelf package."
        : OperatingSystem.IsMacOS()
            ? "This test build currently needs VLC.app in /Applications, or a compatible LibVLC directory supplied through LISTENSHELF_LIBVLC_PATH."
            : OperatingSystem.IsLinux()
                ? "Install your distribution's VLC and LibVLC packages, or supply a compatible LibVLC directory through LISTENSHELF_LIBVLC_PATH."
                : "Install a compatible LibVLC 3 runtime or provide its directory through LISTENSHELF_LIBVLC_PATH.";

    private static void InitializeFromPath(string runtimePath)
    {
        try
        {
            Core.Initialize(runtimePath);
        }
        catch (Exception exception) when (IsNativeRuntimeException(exception))
        {
            throw CreateException(
                $"ListenShelf found LibVLC at {runtimePath}, but it could not be loaded.",
                exception);
        }
    }

    internal static string? FindBundledRuntimePath(
        string baseDirectory,
        DesktopRuntimePlatform platform,
        Architecture architecture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        var libVlcRoot = Path.Combine(baseDirectory, "libvlc");
        var candidates = new List<string>();

        if (platform is DesktopRuntimePlatform.Windows)
        {
            var runtimeFolder = architecture switch
            {
                Architecture.X64 => "win-x64",
                Architecture.X86 => "win-x86",
                Architecture.Arm64 => "win-arm64",
                _ => null,
            };

            if (runtimeFolder is not null)
            {
                candidates.Add(Path.Combine(libVlcRoot, runtimeFolder));
            }
        }

        candidates.Add(libVlcRoot);

        if (platform is DesktopRuntimePlatform.MacOS)
        {
            candidates.Add(Path.GetFullPath(Path.Combine(
                baseDirectory,
                "..",
                "Frameworks",
                "libvlc")));
        }

        return candidates.FirstOrDefault(candidate =>
            ContainsNativeRuntime(candidate, platform));
    }

    private static bool ContainsNativeRuntime(
        string candidate,
        DesktopRuntimePlatform platform)
    {
        if (!Directory.Exists(candidate))
        {
            return false;
        }

        try
        {
            return platform switch
            {
                DesktopRuntimePlatform.Windows =>
                    File.Exists(Path.Combine(candidate, "libvlc.dll")),
                DesktopRuntimePlatform.MacOS =>
                    File.Exists(Path.Combine(candidate, "libvlc.dylib")),
                DesktopRuntimePlatform.Linux =>
                    Directory.EnumerateFiles(candidate, "libvlc.so*").Any(),
                _ => false,
            };
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static LibVlcInitializationException CreateException(
        string message,
        Exception innerException) =>
        new(
            message,
            GetPlatformHelp(),
            RuntimeDescription,
            innerException);

    private static bool IsNativeRuntimeException(Exception exception) =>
        exception is VLCException
            or DllNotFoundException
            or EntryPointNotFoundException
            or BadImageFormatException
            or FileLoadException
            or FileNotFoundException
            or TypeInitializationException;

    private static DesktopRuntimePlatform GetPlatform() => OperatingSystem.IsWindows()
        ? DesktopRuntimePlatform.Windows
        : OperatingSystem.IsMacOS()
            ? DesktopRuntimePlatform.MacOS
            : OperatingSystem.IsLinux()
                ? DesktopRuntimePlatform.Linux
                : DesktopRuntimePlatform.Other;

    private static string GetPlatformName() => GetPlatform() switch
    {
        DesktopRuntimePlatform.Windows => "Windows",
        DesktopRuntimePlatform.MacOS => "macOS",
        DesktopRuntimePlatform.Linux => "Linux",
        _ => RuntimeInformation.OSDescription,
    };
}

internal enum DesktopRuntimePlatform
{
    Windows,
    MacOS,
    Linux,
    Other,
}
