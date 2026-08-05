using ListenShelf.Infrastructure.Backup;
using ListenShelf.Infrastructure.Library;
using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Tests;

public sealed class DatabaseRecoveryServiceTests
{
    [Fact]
    public void RebuildCatalog_PreservesDamagedDatabaseAndRecoversStandardManagedBooks()
    {
        using var workspace = new TestWorkspace();
        var dataRoot = Path.GetDirectoryName(workspace.DatabasePath)!;
        var managedRoot = Path.Combine(dataRoot, "Library");
        var coverRoot = Path.Combine(dataRoot, "Covers");
        var bookId = Guid.NewGuid();
        var bookDirectory = Path.Combine(managedRoot, bookId.ToString("N"));
        Directory.CreateDirectory(bookDirectory);
        Directory.CreateDirectory(coverRoot);
        var audioPath = Path.Combine(bookDirectory, "Recovered Book.m4b");
        var coverPath = Path.Combine(coverRoot, $"{bookId:N}.jpg");
        File.WriteAllBytes(audioPath, [1, 2, 3, 4]);
        File.WriteAllBytes(coverPath, [5, 6, 7]);

        var nonstandardDirectory = Path.Combine(managedRoot, "unknown-folder");
        Directory.CreateDirectory(nonstandardDirectory);
        var nonstandardPath = Path.Combine(nonstandardDirectory, "Needs Storage Care.mp3");
        File.WriteAllBytes(nonstandardPath, [8, 9]);

        byte[] damagedContents = [0x4E, 0x6F, 0x74, 0x53, 0x51, 0x4C, 0x69, 0x74, 0x65];
        Directory.CreateDirectory(dataRoot);
        File.WriteAllBytes(workspace.DatabasePath, damagedContents);
        var service = new DatabaseRecoveryService(workspace.DatabasePath);

        var result = service.RebuildCatalog();

        Assert.Equal(1, result.RecoveredBookCount);
        Assert.Equal(1, result.SkippedAudiobookCount);
        Assert.Equal(
            damagedContents,
            File.ReadAllBytes(Path.Combine(result.PreservedDatabasePath, "listenshelf.db")));
        Assert.True(File.Exists(nonstandardPath));

        var database = new ListenShelfDatabase(workspace.DatabasePath);
        var library = new SqliteAudiobookLibrary(database, managedRoot);
        var recoveredBook = Assert.Single(library.GetBooks());
        Assert.Equal(bookId, recoveredBook.Id);
        Assert.Equal("Recovered Book", recoveredBook.Title);
        Assert.Equal(Path.GetFullPath(audioPath), recoveredBook.FilePath);
        Assert.Equal(Path.GetFullPath(coverPath), recoveredBook.CoverPath);
    }

    [Fact]
    public void RebuildCatalog_WhenFreshDatabaseCannotBeCreated_RestoresDamagedDatabase()
    {
        using var workspace = new TestWorkspace();
        var dataRoot = Path.GetDirectoryName(workspace.DatabasePath)!;
        Directory.CreateDirectory(dataRoot);
        byte[] damagedContents = [0x42, 0x61, 0x64];
        File.WriteAllBytes(workspace.DatabasePath, damagedContents);
        Directory.CreateDirectory(workspace.DatabasePath + "-wal");

        Assert.ThrowsAny<Exception>(() =>
            new DatabaseRecoveryService(workspace.DatabasePath).RebuildCatalog());

        Assert.True(File.Exists(workspace.DatabasePath));
        Assert.Equal(damagedContents, File.ReadAllBytes(workspace.DatabasePath));
    }

    [Fact]
    public void RestoreBackup_PreservesEntireDamagedDataDirectoryBeforeReplacement()
    {
        using var sourceWorkspace = new TestWorkspace();
        var sourceDataRoot = Path.GetDirectoryName(sourceWorkspace.DatabasePath)!;
        var sourceManagedRoot = Path.Combine(sourceDataRoot, "Library");
        var sourceDatabase = new ListenShelfDatabase(sourceWorkspace.DatabasePath);
        var sourceLibrary = new SqliteAudiobookLibrary(sourceDatabase, sourceManagedRoot);
        var sourceFile = sourceWorkspace.CreateSourceFile("Backup Book.m4b", [1, 3, 5, 7]);
        var sourceBook = sourceLibrary.Import(sourceFile).Book;
        var sourceChecker = new SqliteManagedLibraryIntegrityChecker(
            sourceDatabase,
            sourceManagedRoot);
        var backupPath = Path.Combine(
            Path.GetDirectoryName(sourceFile)!,
            "database-recovery.listenshelf-backup");
        _ = new ZipLibraryBackupService(sourceDatabase, sourceChecker).Export(backupPath);

        using var targetWorkspace = new TestWorkspace();
        var targetDataRoot = Path.GetDirectoryName(targetWorkspace.DatabasePath)!;
        var oldManagedRoot = Path.Combine(targetDataRoot, "Library", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(oldManagedRoot);
        var oldAudioPath = Path.Combine(oldManagedRoot, "Preserve Me.mp3");
        byte[] oldAudioContents = [2, 4, 6, 8];
        byte[] damagedDatabaseContents = [0x44, 0x61, 0x6D, 0x61, 0x67, 0x65, 0x64];
        File.WriteAllBytes(oldAudioPath, oldAudioContents);
        File.WriteAllBytes(targetWorkspace.DatabasePath, damagedDatabaseContents);
        var recovery = new DatabaseRecoveryService(targetWorkspace.DatabasePath);

        var result = recovery.RestoreBackup(backupPath);

        Assert.NotNull(result.PreservedDataPath);
        Assert.Equal(
            damagedDatabaseContents,
            File.ReadAllBytes(Path.Combine(result.PreservedDataPath!, "listenshelf.db")));
        Assert.Equal(
            oldAudioContents,
            File.ReadAllBytes(Path.Combine(
                result.PreservedDataPath!,
                Path.GetRelativePath(targetDataRoot, oldAudioPath))));

        var restoredDatabase = new ListenShelfDatabase(targetWorkspace.DatabasePath);
        var restoredLibrary = new SqliteAudiobookLibrary(
            restoredDatabase,
            Path.Combine(targetDataRoot, "Library"));
        var restoredBook = Assert.Single(restoredLibrary.GetBooks());
        Assert.Equal(sourceBook.Id, restoredBook.Id);
        Assert.Equal("Backup Book", restoredBook.Title);
        Assert.Equal([1, 3, 5, 7], File.ReadAllBytes(restoredBook.FilePath));
    }
}
