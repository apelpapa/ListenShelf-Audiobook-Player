using System.Globalization;
using ListenShelf.Application.Backup;
using ListenShelf.Application.Library;
using ListenShelf.Infrastructure.Backup;
using Microsoft.Data.Sqlite;

namespace ListenShelf.Infrastructure.Storage;

public sealed class DatabaseRecoveryService
{
    private static readonly HashSet<string> CoverExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".webp",
        };

    private readonly ZipLibraryBackupService _backupService;
    private readonly string _managedLibraryPath;
    private readonly string _coverCachePath;

    public DatabaseRecoveryService(string? databasePath = null)
    {
        DatabasePath = Path.GetFullPath(
            databasePath ?? ListenShelfDatabase.GetDefaultDatabasePath());
        DataRootPath = Path.GetDirectoryName(DatabasePath)
            ?? throw new InvalidOperationException(
                "The ListenShelf database needs a parent directory.");
        _managedLibraryPath = Path.Combine(DataRootPath, "Library");
        _coverCachePath = Path.Combine(DataRootPath, "Covers");
        _backupService = ZipLibraryBackupService.CreateForDatabaseRecovery(DatabasePath);
    }

    public string DatabasePath { get; }

    public string DataRootPath { get; }

    public LibraryBackupSummary InspectBackup(
        string backupPath,
        IProgress<LibraryBackupProgress>? progress = null) =>
        _backupService.Inspect(backupPath, progress);

    public DatabaseRecoveryRestoreResult RestoreBackup(
        string backupPath,
        IProgress<LibraryBackupProgress>? progress = null) =>
        _backupService.RestoreForDatabaseRecovery(backupPath, progress);

    public DatabaseCatalogRecoveryResult RebuildCatalog()
    {
        var recoveryCandidates = FindRecoverableAudiobooks();
        SqliteConnection.ClearAllPools();
        var preservedDirectory = PreserveDatabaseFiles();
        ListenShelfDatabase database;

        try
        {
            database = new ListenShelfDatabase(DatabasePath);
        }
        catch
        {
            RestorePreservedDatabase(preservedDirectory);
            throw;
        }

        var recoveredCount = 0;
        var skippedCount = 0;
        foreach (var candidate in recoveryCandidates)
        {
            if (candidate.BookId == Guid.Empty)
            {
                skippedCount++;
                continue;
            }

            try
            {
                InsertRecoveredBook(database, candidate);
                recoveredCount++;
            }
            catch (Exception exception) when (exception is SqliteException
                                               or IOException
                                               or UnauthorizedAccessException
                                               or ArgumentException
                                               or InvalidOperationException)
            {
                skippedCount++;
            }
        }

        return new DatabaseCatalogRecoveryResult(
            recoveredCount,
            skippedCount,
            preservedDirectory);
    }

    private string PreserveDatabaseFiles()
    {
        Directory.CreateDirectory(DataRootPath);
        var timestamp = DateTimeOffset.Now.ToString(
            "yyyy-MM-dd HH-mm-ss",
            CultureInfo.InvariantCulture);
        var preferredPath = Path.Combine(
            DataRootPath,
            "Database Recovery",
            $"Damaged database {timestamp}");
        var recoveryPath = CreateUniqueDirectory(preferredPath);

        var movedAnyFile = false;
        try
        {
            foreach (var databaseFile in GetDatabaseFamilyPaths())
            {
                if (!File.Exists(databaseFile))
                {
                    continue;
                }

                File.Move(databaseFile, Path.Combine(recoveryPath, Path.GetFileName(databaseFile)));
                movedAnyFile = true;
            }

            if (!movedAnyFile)
            {
                throw new FileNotFoundException(
                    "The database files are no longer present. Retry starting ListenShelf before rebuilding the catalog.",
                    DatabasePath);
            }

            return recoveryPath;
        }
        catch
        {
            RestorePreservedDatabase(recoveryPath);
            throw;
        }
    }

    private void RestorePreservedDatabase(string recoveryPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var currentFile in GetDatabaseFamilyPaths())
        {
            if (File.Exists(currentFile))
            {
                File.Delete(currentFile);
            }
        }

        if (!Directory.Exists(recoveryPath))
        {
            return;
        }

        foreach (var preservedFile in Directory.EnumerateFiles(recoveryPath))
        {
            var destination = Path.Combine(DataRootPath, Path.GetFileName(preservedFile));
            File.Move(preservedFile, destination);
        }

        if (!Directory.EnumerateFileSystemEntries(recoveryPath).Any())
        {
            Directory.Delete(recoveryPath);
        }
    }

    private IReadOnlyList<RecoveryCandidate> FindRecoverableAudiobooks()
    {
        var candidates = new List<RecoveryCandidate>();
        if (!Directory.Exists(_managedLibraryPath))
        {
            return candidates;
        }

        EnsureNotReparsePoint(_managedLibraryPath);
        foreach (var directory in Directory.EnumerateDirectories(_managedLibraryPath))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0
                || !Guid.TryParseExact(Path.GetFileName(directory), "N", out var bookId))
            {
                AddSkippedCandidates(directory, candidates);
                continue;
            }

            var audioFiles = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .Where(path => AudiobookFileFormats.IsSupported(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (audioFiles.Length == 0)
            {
                continue;
            }

            var audioPath = Path.GetFullPath(audioFiles[0]);
            EnsureNotReparsePoint(audioPath);
            candidates.Add(new RecoveryCandidate(
                bookId,
                audioPath,
                FindCoverPath(bookId)));

            for (var index = 1; index < audioFiles.Length; index++)
            {
                candidates.Add(new RecoveryCandidate(Guid.Empty, audioFiles[index], null));
            }
        }

        return candidates;

        static void AddSkippedCandidates(
            string directory,
            List<RecoveryCandidate> foundCandidates)
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                return;
            }

            foreach (var audioPath in Directory.EnumerateFiles(
                         directory,
                         "*",
                         SearchOption.TopDirectoryOnly).Where(AudiobookFileFormats.IsSupported))
            {
                foundCandidates.Add(new RecoveryCandidate(Guid.Empty, audioPath, null));
            }
        }
    }

    private string? FindCoverPath(Guid bookId)
    {
        if (!Directory.Exists(_coverCachePath))
        {
            return null;
        }

        EnsureNotReparsePoint(_coverCachePath);
        return Directory.EnumerateFiles(
                _coverCachePath,
                $"{bookId:N}.*",
                SearchOption.TopDirectoryOnly)
            .Where(path => CoverExtensions.Contains(Path.GetExtension(path)))
            .Where(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(Path.GetFullPath)
            .FirstOrDefault();
    }

    private static void InsertRecoveredBook(
        ListenShelfDatabase database,
        RecoveryCandidate candidate)
    {
        var file = new FileInfo(candidate.AudioPath);
        var title = AudiobookMetadata.FromFileName(
            Path.GetFileNameWithoutExtension(file.Name)).Title;
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO library_books (
                book_id,
                title,
                file_path,
                file_key,
                storage_mode,
                source_path,
                source_key,
                file_size_bytes,
                added_utc,
                cover_path)
            VALUES (
                $book_id,
                $title,
                $file_path,
                $file_key,
                'Managed',
                NULL,
                NULL,
                $file_size_bytes,
                $added_utc,
                $cover_path);
            """;
        command.Parameters.AddWithValue("$book_id", candidate.BookId.ToString("D"));
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$file_path", file.FullName);
        command.Parameters.AddWithValue("$file_key", CreatePathKey(file.FullName));
        command.Parameters.AddWithValue("$file_size_bytes", file.Length);
        command.Parameters.AddWithValue(
            "$added_utc",
            new DateTimeOffset(file.CreationTimeUtc, TimeSpan.Zero)
                .ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue(
            "$cover_path",
            (object?)candidate.CoverPath ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private IEnumerable<string> GetDatabaseFamilyPaths()
    {
        yield return DatabasePath;
        yield return DatabasePath + "-wal";
        yield return DatabasePath + "-shm";
    }

    private static string CreateUniqueDirectory(string preferredPath)
    {
        var path = preferredPath;
        while (Directory.Exists(path) || File.Exists(path))
        {
            path = $"{preferredPath} {Guid.NewGuid():N}";
        }

        Directory.CreateDirectory(path);
        return path;
    }

    private static void EnsureNotReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                "ListenShelf will not automatically recover symbolic links or filesystem junctions.");
        }
    }

    private static string CreatePathKey(string path)
    {
        var normalizedPath = Path.GetFullPath(path);
        return OperatingSystem.IsWindows()
            ? normalizedPath.ToUpperInvariant()
            : normalizedPath;
    }

    private sealed record RecoveryCandidate(
        Guid BookId,
        string AudioPath,
        string? CoverPath);
}
