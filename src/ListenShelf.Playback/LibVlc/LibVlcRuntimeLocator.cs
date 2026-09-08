using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

namespace ListenShelf.Playback.LibVlc;

public static class LibVlcRuntimeLocator
{
    public const string CustomRuntimePathEnvironmentVariable =
        "LISTENSHELF_LIBVLC_PATH";

    private const string VlcPluginPathEnvironmentVariable = "VLC_PLUGIN_PATH";

    public static string RuntimeDescription =>
        $"{GetPlatformName()} {RuntimeInformation.ProcessArchitecture} • .NET {Environment.Version}";

    public static void Initialize()
    {
        // Android passes its own initializer to LibVlcAudioEngine and never uses this locator.
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The default LibVLC locator supports the Windows desktop app only.");

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
            RuntimeInformation.ProcessArchitecture);
        if (bundledPath is not null)
        {
            ConfigureBundledPluginPath(bundledPath);
            InitializeFromPath(bundledPath);
            return;
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
        : "The host application must supply and initialize a compatible LibVLC runtime. Android includes its runtime in the APK.";

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
        Architecture architecture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        var libVlcRoot = Path.Combine(baseDirectory, "libvlc");
        var candidates = new List<string>();

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

        candidates.Add(libVlcRoot);
        return candidates.FirstOrDefault(ContainsNativeRuntime);
    }

    private static bool ContainsNativeRuntime(string candidate)
    {
        if (!Directory.Exists(candidate))
        {
            return false;
        }

        try
        {
            return File.Exists(Path.Combine(candidate, "libvlc.dll"));
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

    private static void ConfigureBundledPluginPath(string runtimePath)
    {
        var pluginPath = Path.Combine(runtimePath, "plugins");

        if (Directory.Exists(pluginPath))
        {
            Environment.SetEnvironmentVariable(
                VlcPluginPathEnvironmentVariable,
                pluginPath);
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

    private static string GetPlatformName() => OperatingSystem.IsWindows()
        ? "Windows" : OperatingSystem.IsAndroid() ? "Android" : "Other";
}
