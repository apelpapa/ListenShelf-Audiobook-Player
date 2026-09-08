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
        // Android supplies its app-private database path explicitly; this is the Windows default.
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The default ListenShelf data directory is Windows-only. Other hosts must supply their own database path.");

        return CreateForWindows(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.Create));
    }

    public static ListenShelfPaths CreateForWindows(string? localApplicationDataPath)
    {
        return new ListenShelfPaths(Path.Combine(
            RequireAbsolutePath(
                localApplicationDataPath,
                "The local application-data directory is unavailable."),
            "ListenShelf"));
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
