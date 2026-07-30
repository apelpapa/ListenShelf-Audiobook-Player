using Microsoft.Data.Sqlite;

namespace ListenShelf.Tests;

internal sealed class TestWorkspace : IDisposable
{
    private readonly string _rootPath = Path.Combine(
        Path.GetTempPath(),
        "ListenShelf.Tests",
        Guid.NewGuid().ToString("N"));

    public TestWorkspace()
    {
        Directory.CreateDirectory(_rootPath);
    }

    public string DatabasePath => Path.Combine(_rootPath, "data", "listenshelf.db");

    public string ManagedLibraryPath => Path.Combine(_rootPath, "managed-library");

    public string CreateSourceFile(string fileName, ReadOnlySpan<byte> contents)
    {
        var sourceDirectory = Path.Combine(_rootPath, "source");
        Directory.CreateDirectory(sourceDirectory);

        var filePath = Path.Combine(sourceDirectory, fileName);
        File.WriteAllBytes(filePath, contents);
        return filePath;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }
}
