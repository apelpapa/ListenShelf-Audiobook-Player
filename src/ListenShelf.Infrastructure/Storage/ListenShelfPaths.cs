namespace ListenShelf.Infrastructure.Storage;

public sealed record ListenShelfPaths
{
    private ListenShelfPaths(string dataRootPath)
    {
        DataRootPath = Path.GetFullPath(dataRootPath);
        DatabasePath = Path.Combine(DataRootPath, "listenshelf.db");
        ManagedLibraryPath = Path.Combine(DataRootPath, "Library");
        CoverCachePath = Path.Combine(DataRootPath, "Covers");
        LogDirectoryPath = Path.Combine(DataRootPath, "Logs");
    }

    public string DataRootPath { get; }

    public string DatabasePath { get; }

    public string ManagedLibraryPath { get; }

    public string CoverCachePath { get; }

    public string LogDirectoryPath { get; }

    public static ListenShelfPaths CreateDefault()
    {
        var platform = OperatingSystem.IsWindows()
            ? DesktopPlatformKind.Windows
            : OperatingSystem.IsMacOS()
                ? DesktopPlatformKind.MacOS
                : OperatingSystem.IsLinux()
                    ? DesktopPlatformKind.Linux
                    : throw new PlatformNotSupportedException(
                        "ListenShelf currently supports Windows, macOS, and Linux desktop systems.");

        return CreateForPlatform(
            platform,
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.Create),
            Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile,
                Environment.SpecialFolderOption.DoNotVerify),
            Environment.GetEnvironmentVariable("XDG_DATA_HOME"));
    }

    public static ListenShelfPaths CreateForPlatform(
        DesktopPlatformKind platform,
        string? localApplicationDataPath,
        string? userProfilePath,
        string? xdgDataHome = null)
    {
        var dataRoot = platform switch
        {
            DesktopPlatformKind.Windows => Path.Combine(
                RequireAbsolutePath(
                    localApplicationDataPath,
                    "The local application-data directory is unavailable."),
                "ListenShelf"),
            DesktopPlatformKind.MacOS => Path.Combine(
                RequireAbsolutePath(
                    userProfilePath,
                    "The user profile directory is unavailable."),
                "Library",
                "Application Support",
                "ListenShelf"),
            DesktopPlatformKind.Linux => Path.Combine(
                GetLinuxDataHome(xdgDataHome, userProfilePath),
                "ListenShelf"),
            _ => throw new ArgumentOutOfRangeException(
                nameof(platform),
                platform,
                "Unsupported desktop platform."),
        };

        return new ListenShelfPaths(dataRoot);
    }

    private static string GetLinuxDataHome(
        string? xdgDataHome,
        string? userProfilePath)
    {
        if (!string.IsNullOrWhiteSpace(xdgDataHome)
            && Path.IsPathFullyQualified(xdgDataHome))
        {
            return Path.GetFullPath(xdgDataHome);
        }

        return Path.Combine(
            RequireAbsolutePath(
                userProfilePath,
                "The user profile directory is unavailable."),
            ".local",
            "share");
    }

    private static string RequireAbsolutePath(string? path, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new InvalidOperationException(errorMessage);
        }

        return Path.GetFullPath(path);
    }
}
