using System.IO.Compression;
using ListenShelf.Application.Bookmarks;
using ListenShelf.Application.Progress;
using ListenShelf.Application.Settings;
using ListenShelf.Infrastructure.Backup;
using ListenShelf.Infrastructure.Bookmarks;
using ListenShelf.Infrastructure.Library;
using ListenShelf.Infrastructure.Progress;
using ListenShelf.Infrastructure.Settings;
using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Tests;

public sealed class LibraryBackupServiceTests
{
    [Fact]
    public void Export_CreatesOneValidatedArchiveWithCatalogDataAndManagedFiles()
    {
        using var workspace = new TestWorkspace();
        var environment = CreateEnvironment(workspace);
        var sourcePath = workspace.CreateSourceFile(
            "Backup Story.m4b",
            [1, 2, 3, 4, 5, 6]);
        var book = environment.Library.Import(sourcePath).Book;
        var coverSource = workspace.CreateSourceFile("cover.png", [9, 8, 7, 6]);
        environment.Library.SetCover(book.Id, coverSource);
        var orphanPath = Path.Combine(environment.ManagedLibraryPath, "orphan.txt");
        File.WriteAllText(orphanPath, "preserve me");
        var destinationPath = Path.Combine(GetWorkspaceRoot(workspace), "exports", "My Library");

        var summary = environment.BackupService.Export(destinationPath);
        var inspected = environment.BackupService.Inspect(summary.BackupPath);

        Assert.EndsWith(
            ZipLibraryBackupService.BackupFileExtension,
            summary.BackupPath,
            StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(summary.BackupPath));
        Assert.True(summary.IsComplete);
        Assert.Equal(1, summary.BookCount);
        Assert.Equal(summary.BookCount, inspected.BookCount);
        Assert.Equal(summary.FileCount, inspected.FileCount);
        Assert.Equal(summary.UncompressedSizeBytes, inspected.UncompressedSizeBytes);
        Assert.Equal(summary.ArchiveSizeBytes, inspected.ArchiveSizeBytes);
        Assert.True(File.Exists(sourcePath));
        Assert.True(File.Exists(book.FilePath));

        using var archive = ZipFile.OpenRead(summary.BackupPath);
        Assert.Contains(archive.Entries, entry => entry.FullName == "manifest.json");
        Assert.Contains(archive.Entries, entry => entry.FullName == "data/listenshelf.db");
        Assert.Contains(archive.Entries, entry => entry.FullName.EndsWith(
            "/Backup Story.m4b",
            StringComparison.Ordinal));
        Assert.Contains(archive.Entries, entry => entry.FullName.EndsWith(
            "/orphan.txt",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Restore_ReplacesTheLibraryRebasesPathsAndCreatesASafetyBackup()
    {
        using var sourceWorkspace = new TestWorkspace();
        var source = CreateEnvironment(sourceWorkspace);
        var audioContents = new byte[] { 10, 20, 30, 40, 50 };
        var originalSourcePath = sourceWorkspace.CreateSourceFile(
            "Restored Story.mp3",
            audioContents);
        var sourceBook = source.Library.Import(originalSourcePath).Book;
        var coverContents = new byte[] { 1, 3, 5, 7 };
        var coverSourcePath = sourceWorkspace.CreateSourceFile("restored-cover.jpg", coverContents);
        source.Library.SetCover(sourceBook.Id, coverSourcePath);
        var savedAtUtc = new DateTimeOffset(2026, 8, 3, 1, 2, 3, TimeSpan.Zero);
        new SqlitePlaybackProgressStore(source.Database).Save(new PlaybackProgress(
            sourceBook.FilePath,
            TimeSpan.FromMinutes(12),
            TimeSpan.FromHours(8),
            savedAtUtc));
        var bookmarkId = Guid.NewGuid();
        new SqlitePlaybackBookmarkStore(source.Database).Save(new PlaybackBookmark(
            bookmarkId,
            sourceBook.FilePath,
            TimeSpan.FromMinutes(10),
            "Important",
            "Remember this",
            2,
            "Chapter 3",
            savedAtUtc,
            savedAtUtc));
        new SqliteAppSettingsStore(source.Database).SaveTheme(AppTheme.Light);
        var backupPath = Path.Combine(
            GetWorkspaceRoot(sourceWorkspace),
            "exports",
            "Source Library.listenshelf-backup");
        var exported = source.BackupService.Export(backupPath);

        using var targetWorkspace = new TestWorkspace();
        var target = CreateEnvironment(targetWorkspace);
        var replacedSourcePath = targetWorkspace.CreateSourceFile(
            "Replaced Book.m4b",
            [90, 91, 92]);
        var replacedBook = target.Library.Import(replacedSourcePath).Book;
        File.Delete(replacedBook.FilePath);

        var result = target.BackupService.Restore(exported.BackupPath);

        Assert.True(File.Exists(result.SafetyBackupPath));
        Assert.False(result.RollbackCleanupPending);
        var safetySummary = target.BackupService.Inspect(result.SafetyBackupPath);
        Assert.Equal(1, safetySummary.BookCount);
        Assert.False(safetySummary.IsComplete);

        var restoredBook = Assert.Single(target.Library.GetBooks());
        Assert.Equal(sourceBook.Id, restoredBook.Id);
        Assert.False(PathsEqual(sourceBook.FilePath, restoredBook.FilePath));
        Assert.StartsWith(
            target.ManagedLibraryPath,
            restoredBook.FilePath,
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
        Assert.Equal(audioContents, File.ReadAllBytes(restoredBook.FilePath));
        Assert.False(File.Exists(replacedBook.FilePath));
        Assert.True(File.Exists(replacedSourcePath));
        Assert.True(File.Exists(originalSourcePath));
        Assert.NotNull(restoredBook.CoverPath);
        Assert.Equal(coverContents, File.ReadAllBytes(restoredBook.CoverPath!));

        var restoredProgress = new SqlitePlaybackProgressStore(target.Database)
            .Get(restoredBook.FilePath);
        Assert.NotNull(restoredProgress);
        Assert.Equal(TimeSpan.FromMinutes(12), restoredProgress.Position);
        var restoredBookmark = Assert.Single(
            new SqlitePlaybackBookmarkStore(target.Database)
                .GetForFile(restoredBook.FilePath));
        Assert.Equal(bookmarkId, restoredBookmark.Id);
        Assert.Equal("Remember this", restoredBookmark.Note);
        Assert.Equal(AppTheme.Light, new SqliteAppSettingsStore(target.Database).GetTheme());
        Assert.True(target.Checker.Check().IsHealthy);
    }

    [Fact]
    public void Restore_RejectsATamperedArchiveBeforeChangingTheCurrentLibrary()
    {
        using var sourceWorkspace = new TestWorkspace();
        var source = CreateEnvironment(sourceWorkspace);
        source.Library.Import(sourceWorkspace.CreateSourceFile(
            "Tamper Target.m4b",
            [1, 2, 3, 4]));
        var backup = source.BackupService.Export(Path.Combine(
            GetWorkspaceRoot(sourceWorkspace),
            "exports",
            "tampered.listenshelf-backup"));

        using (var archive = ZipFile.Open(backup.BackupPath, ZipArchiveMode.Update))
        {
            var audioEntry = Assert.Single(
                archive.Entries,
                entry => entry.FullName.EndsWith(
                    ".m4b",
                    StringComparison.OrdinalIgnoreCase));
            var entryName = audioEntry.FullName;
            audioEntry.Delete();
            var replacement = archive.CreateEntry(entryName, CompressionLevel.NoCompression);
            using var stream = replacement.Open();
            stream.Write([4, 3, 2, 1]);
        }

        using var targetWorkspace = new TestWorkspace();
        var target = CreateEnvironment(targetWorkspace);
        var currentBook = target.Library.Import(targetWorkspace.CreateSourceFile(
            "Current Book.mp3",
            [8, 8, 8, 8])).Book;

        var exception = Assert.Throws<InvalidDataException>(() =>
            target.BackupService.Restore(backup.BackupPath));

        Assert.Contains("integrity check", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(currentBook.Id, Assert.Single(target.Library.GetBooks()).Id);
        Assert.True(File.Exists(currentBook.FilePath));
        Assert.Empty(Directory.EnumerateFiles(
            Path.GetDirectoryName(backup.BackupPath)!,
            $"ListenShelf before restore *{ZipLibraryBackupService.BackupFileExtension}"));
    }

    [Fact]
    public void Export_RefusesToClaimACompleteBackupWhenACatalogedFileIsMissing()
    {
        using var workspace = new TestWorkspace();
        var environment = CreateEnvironment(workspace);
        var book = environment.Library.Import(workspace.CreateSourceFile(
            "Missing Book.m4a",
            [1, 2, 3])).Book;
        File.Delete(book.FilePath);
        var destinationPath = Path.Combine(
            GetWorkspaceRoot(workspace),
            "exports",
            "incomplete.listenshelf-backup");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            environment.BackupService.Export(destinationPath));

        Assert.Contains("missing", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(destinationPath));
    }

    private static BackupEnvironment CreateEnvironment(TestWorkspace workspace)
    {
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        var dataRoot = Path.GetDirectoryName(database.DatabasePath)!;
        var managedLibraryPath = Path.Combine(dataRoot, "Library");
        var library = new SqliteAudiobookLibrary(database, managedLibraryPath);
        var checker = new SqliteManagedLibraryIntegrityChecker(database, managedLibraryPath);
        var backupService = new ZipLibraryBackupService(database, checker);
        return new BackupEnvironment(
            database,
            managedLibraryPath,
            library,
            checker,
            backupService);
    }

    private static string GetWorkspaceRoot(TestWorkspace workspace) =>
        Directory.GetParent(Path.GetDirectoryName(workspace.DatabasePath)!)!.FullName;

    private static bool PathsEqual(string firstPath, string secondPath) =>
        string.Equals(
            Path.GetFullPath(firstPath),
            Path.GetFullPath(secondPath),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    private sealed record BackupEnvironment(
        ListenShelfDatabase Database,
        string ManagedLibraryPath,
        SqliteAudiobookLibrary Library,
        SqliteManagedLibraryIntegrityChecker Checker,
        ZipLibraryBackupService BackupService);
}
