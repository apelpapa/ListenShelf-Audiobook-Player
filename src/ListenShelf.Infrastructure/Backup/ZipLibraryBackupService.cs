using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ListenShelf.Application.Backup;
using ListenShelf.Application.Library;
using ListenShelf.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace ListenShelf.Infrastructure.Backup;

public sealed class ZipLibraryBackupService : ILibraryBackupService
{
    public const string BackupFileExtension = ".listenshelf-backup";

    private const string BackupFormatName = "ListenShelf.Backup";
    private const int CurrentFormatVersion = 1;
    private const string ManifestArchivePath = "manifest.json";
    private const string DatabaseArchivePath = "data/listenshelf.db";
    private const int MaximumManifestBytes = 16 * 1024 * 1024;
    private const long ProgressReportIntervalBytes = 16L * 1024L * 1024L;
    private static readonly StringComparer ArchivePathComparer =
        StringComparer.OrdinalIgnoreCase;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly ListenShelfDatabase? _database;
    private readonly string _managedLibraryPath;
    private readonly string _coverCachePath;
    private readonly StringComparison _pathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public ZipLibraryBackupService(
        ListenShelfDatabase database,
        IManagedLibraryIntegrityChecker integrityChecker)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        ArgumentNullException.ThrowIfNull(integrityChecker);

        DataRootPath = Path.GetDirectoryName(_database.DatabasePath)
            ?? throw new InvalidOperationException(
                "The ListenShelf database needs a parent directory.");
        DataRootPath = Path.GetFullPath(DataRootPath);
        _managedLibraryPath = Path.GetFullPath(integrityChecker.ManagedLibraryPath);
        _coverCachePath = Path.GetFullPath(Path.Combine(DataRootPath, "Covers"));

        EnsurePathIsInsideDataRoot(_managedLibraryPath, "managed library");
        EnsurePathIsInsideDataRoot(_coverCachePath, "cover cache");
    }

    private ZipLibraryBackupService(string databasePath)
    {
        var normalizedDatabasePath = Path.GetFullPath(databasePath);
        DataRootPath = Path.GetDirectoryName(normalizedDatabasePath)
            ?? throw new InvalidOperationException(
                "The ListenShelf database needs a parent directory.");
        DataRootPath = Path.GetFullPath(DataRootPath);
        _managedLibraryPath = Path.GetFullPath(Path.Combine(DataRootPath, "Library"));
        _coverCachePath = Path.GetFullPath(Path.Combine(DataRootPath, "Covers"));
    }

    public static ZipLibraryBackupService CreateForDatabaseRecovery(
        string databasePath) =>
        new(databasePath);

    public string DataRootPath { get; }

    public LibraryBackupSummary Export(
        string destinationPath,
        IProgress<LibraryBackupProgress>? progress = null)
    {
        EnsureNormalLibraryIsAvailable();
        return CreateBackup(destinationPath, allowIncompleteCatalog: false, progress);
    }

    public LibraryBackupSummary Inspect(
        string backupPath,
        IProgress<LibraryBackupProgress>? progress = null)
    {
        var normalizedPath = NormalizeExistingBackupPath(backupPath);
        EnsurePathIsOutsideDataRoot(normalizedPath, "The selected backup");
        using var stream = new FileStream(
            normalizedPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var manifest = ReadAndValidateManifest(archive);
        ValidateArchiveContents(archive, manifest, progress, extractRoot: null);
        ValidateDatabaseEntry(archive, manifest);
        return CreateSummary(normalizedPath, manifest, stream.Length);
    }

    public LibraryRestoreResult Restore(
        string backupPath,
        IProgress<LibraryBackupProgress>? progress = null)
    {
        EnsureNormalLibraryIsAvailable();
        var normalizedBackupPath = NormalizeExistingBackupPath(backupPath);
        EnsurePathIsOutsideDataRoot(normalizedBackupPath, "The selected backup");

        var dataRootParent = Path.GetDirectoryName(DataRootPath)
            ?? throw new InvalidOperationException(
                "The ListenShelf data directory needs a parent directory.");
        var operationId = Guid.NewGuid().ToString("N");
        var stagingRoot = Path.Combine(dataRootParent, $".ListenShelf.restore.{operationId}");
        var rollbackRoot = Path.Combine(dataRootParent, $".ListenShelf.rollback.{operationId}");
        var stagedDataRoot = Path.Combine(stagingRoot, "data");
        var safetyBackupPath = CreateSafetyBackupPath(normalizedBackupPath);
        var rollbackCleanupPending = false;
        LibraryBackupSummary restoredSummary;

        Directory.CreateDirectory(stagingRoot);
        try
        {
            BackupManifest manifest;
            using (var stream = new FileStream(
                       normalizedBackupPath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false))
            {
                manifest = ReadAndValidateManifest(archive);
                ValidateArchiveContents(archive, manifest, progress, stagingRoot);
                restoredSummary = CreateSummary(
                    normalizedBackupPath,
                    manifest,
                    stream.Length);
            }

            var stagedDatabasePath = Path.Combine(stagedDataRoot, "listenshelf.db");
            ValidateAndRebaseDatabase(stagedDatabasePath, stagedDataRoot, manifest);
            _ = new ListenShelfDatabase(
                stagedDatabasePath,
                createMigrationSafetyCopy: false);

            progress?.Report(new LibraryBackupProgress(
                "Creating a safety backup of the current library",
                0,
                0,
                0,
                0));
            _ = CreateBackup(
                safetyBackupPath,
                allowIncompleteCatalog: true,
                progress);

            SqliteConnection.ClearAllPools();
            var movedCurrentData = false;
            try
            {
                if (Directory.Exists(DataRootPath))
                {
                    Directory.Move(DataRootPath, rollbackRoot);
                    movedCurrentData = true;
                }

                Directory.Move(stagedDataRoot, DataRootPath);
                ValidateLiveDatabase();
            }
            catch
            {
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(DataRootPath))
                {
                    Directory.Delete(DataRootPath, recursive: true);
                }

                if (movedCurrentData && Directory.Exists(rollbackRoot))
                {
                    Directory.Move(rollbackRoot, DataRootPath);
                }

                throw;
            }

            if (Directory.Exists(rollbackRoot))
            {
                try
                {
                    Directory.Delete(rollbackRoot, recursive: true);
                }
                catch (Exception exception) when (IsFilesystemException(exception))
                {
                    rollbackCleanupPending = true;
                }
            }

            progress?.Report(new LibraryBackupProgress(
                "Restore complete",
                restoredSummary.FileCount,
                restoredSummary.FileCount,
                restoredSummary.UncompressedSizeBytes,
                restoredSummary.UncompressedSizeBytes));

            return new LibraryRestoreResult(
                restoredSummary,
                safetyBackupPath,
                rollbackCleanupPending);
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
            {
                try
                {
                    Directory.Delete(stagingRoot, recursive: true);
                }
                catch (Exception exception) when (IsFilesystemException(exception))
                {
                    // The live library is never stored in this staging directory.
                }
            }
        }
    }

    public DatabaseRecoveryRestoreResult RestoreForDatabaseRecovery(
        string backupPath,
        IProgress<LibraryBackupProgress>? progress = null)
    {
        var normalizedBackupPath = NormalizeExistingBackupPath(backupPath);
        EnsurePathIsOutsideDataRoot(normalizedBackupPath, "The selected backup");

        var dataRootParent = Path.GetDirectoryName(DataRootPath)
            ?? throw new InvalidOperationException(
                "The ListenShelf data directory needs a parent directory.");
        var operationId = Guid.NewGuid().ToString("N");
        var stagingRoot = Path.Combine(dataRootParent, $".ListenShelf.recovery.{operationId}");
        var stagedDataRoot = Path.Combine(stagingRoot, "data");
        var preservedDataRoot = CreatePreservedDataPath(dataRootParent);
        LibraryBackupSummary restoredSummary;

        Directory.CreateDirectory(stagingRoot);
        try
        {
            BackupManifest manifest;
            using (var stream = new FileStream(
                       normalizedBackupPath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false))
            {
                manifest = ReadAndValidateManifest(archive);
                ValidateArchiveContents(archive, manifest, progress, stagingRoot);
                restoredSummary = CreateSummary(
                    normalizedBackupPath,
                    manifest,
                    stream.Length);
            }

            var stagedDatabasePath = Path.Combine(stagedDataRoot, "listenshelf.db");
            ValidateAndRebaseDatabase(stagedDatabasePath, stagedDataRoot, manifest);
            _ = new ListenShelfDatabase(
                stagedDatabasePath,
                createMigrationSafetyCopy: false);

            progress?.Report(new LibraryBackupProgress(
                "Preserving the current data directory",
                0,
                0,
                0,
                0));

            SqliteConnection.ClearAllPools();
            var movedCurrentData = false;
            try
            {
                if (Directory.Exists(DataRootPath))
                {
                    Directory.Move(DataRootPath, preservedDataRoot);
                    movedCurrentData = true;
                }

                Directory.Move(stagedDataRoot, DataRootPath);
                ValidateLiveDatabase();
            }
            catch
            {
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(DataRootPath))
                {
                    Directory.Delete(DataRootPath, recursive: true);
                }

                if (movedCurrentData && Directory.Exists(preservedDataRoot))
                {
                    Directory.Move(preservedDataRoot, DataRootPath);
                }

                throw;
            }

            progress?.Report(new LibraryBackupProgress(
                "Recovery restore complete",
                restoredSummary.FileCount,
                restoredSummary.FileCount,
                restoredSummary.UncompressedSizeBytes,
                restoredSummary.UncompressedSizeBytes));

            return new DatabaseRecoveryRestoreResult(
                restoredSummary,
                movedCurrentData ? preservedDataRoot : null);
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
            {
                try
                {
                    Directory.Delete(stagingRoot, recursive: true);
                }
                catch (Exception exception) when (IsFilesystemException(exception))
                {
                    // The live or preserved library is never kept in staging.
                }
            }
        }
    }

    private LibraryBackupSummary CreateBackup(
        string destinationPath,
        bool allowIncompleteCatalog,
        IProgress<LibraryBackupProgress>? progress)
    {
        var normalizedDestination = NormalizeDestinationPath(destinationPath);
        EnsurePathIsOutsideDataRoot(normalizedDestination, "The backup destination");

        var destinationDirectory = Path.GetDirectoryName(normalizedDestination)
            ?? throw new InvalidOperationException(
                "The backup destination needs a parent directory.");
        Directory.CreateDirectory(destinationDirectory);

        var operationId = Guid.NewGuid().ToString("N");
        var temporaryArchivePath = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileName(normalizedDestination)}.{operationId}.exporting");
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "ListenShelf.Backup",
            operationId);
        var databaseSnapshotPath = Path.Combine(temporaryRoot, "listenshelf.db");
        Directory.CreateDirectory(temporaryRoot);

        try
        {
            progress?.Report(new LibraryBackupProgress(
                "Creating a consistent database snapshot",
                0,
                0,
                0,
                0));
            CreateDatabaseSnapshot(databaseSnapshotPath);

            var catalogBooks = ReadCatalogBooks(databaseSnapshotPath);
            var sources = BuildArchiveSources(databaseSnapshotPath);
            var sourcePaths = sources
                .Select(source => source.ArchivePath)
                .ToHashSet(StringComparer.Ordinal);
            var manifestBooks = new List<BackupBookManifest>(catalogBooks.Count);
            var completeCatalog = true;

            foreach (var book in catalogBooks)
            {
                var audioArchivePath = ToDataArchivePath(
                    book.FilePath,
                    allowIncompleteCatalog
                        ? $"data/Library/{book.BookId:N}/{GetSafeArchiveFileName(book.FilePath, "audiobook.missing")}"
                        : null);
                var audioIsPresent = sourcePaths.Contains(audioArchivePath);
                completeCatalog &= audioIsPresent;

                string? coverArchivePath = null;
                if (!string.IsNullOrWhiteSpace(book.CoverPath))
                {
                    if (allowIncompleteCatalog)
                    {
                        coverArchivePath = ToDataArchivePath(
                            book.CoverPath,
                            $"data/Covers/{GetSafeArchiveFileName(book.CoverPath, $"{book.BookId:N}.cover.missing")}");
                        completeCatalog &= sourcePaths.Contains(coverArchivePath);
                    }
                    else
                    {
                        try
                        {
                            var candidateCoverPath = ToDataArchivePath(book.CoverPath);
                            if (sourcePaths.Contains(candidateCoverPath))
                            {
                                coverArchivePath = candidateCoverPath;
                            }
                        }
                        catch (InvalidOperationException)
                        {
                            // A missing or unsafe cached cover must not prevent backing up the audiobook.
                        }
                    }
                }

                manifestBooks.Add(new BackupBookManifest(
                    book.BookId,
                    audioArchivePath,
                    coverArchivePath));
            }

            if (!completeCatalog && !allowIncompleteCatalog)
            {
                throw new InvalidOperationException(
                    "The library contains a cataloged audiobook or cover that is missing from managed storage. Review Storage Care before creating a normal backup.");
            }

            var pendingRemovalCount = ReadPendingRemovalCount(databaseSnapshotPath);
            if (pendingRemovalCount > 0 && !allowIncompleteCatalog)
            {
                throw new InvalidOperationException(
                    "A confirmed removal is still waiting for cleanup. Review Storage Care before creating a normal backup.");
            }

            var directories = BuildArchiveDirectories();
            var totalBytes = sources.Sum(source => source.Length);
            var completedBytes = 0L;
            var completedFiles = 0;
            var manifestFiles = new List<BackupFileManifest>(sources.Count);
            var createdAtUtc = DateTimeOffset.UtcNow;

            using (var fileStream = new FileStream(
                       temporaryArchivePath,
                       FileMode.CreateNew,
                       FileAccess.ReadWrite,
                       FileShare.None,
                       bufferSize: 1024 * 1024,
                       FileOptions.WriteThrough))
            {
                using (var archive = new ZipArchive(
                           fileStream,
                           ZipArchiveMode.Create,
                           leaveOpen: true))
                {
                    foreach (var source in sources)
                    {
                        var fileManifest = AddFileToArchive(
                            archive,
                            source,
                            totalBytes,
                            ref completedBytes,
                            ref completedFiles,
                            sources.Count,
                            progress);
                        manifestFiles.Add(fileManifest);
                    }

                    var manifest = new BackupManifest(
                        BackupFormatName,
                        CurrentFormatVersion,
                        createdAtUtc,
                        GetApplicationVersion(),
                        completeCatalog,
                        catalogBooks.Count,
                        DatabaseArchivePath,
                        directories,
                        manifestBooks,
                        manifestFiles);
                    var manifestEntry = archive.CreateEntry(
                        ManifestArchivePath,
                        CompressionLevel.Optimal);
                    using var manifestStream = manifestEntry.Open();
                    JsonSerializer.Serialize(manifestStream, manifest, JsonOptions);
                }

                fileStream.Flush(flushToDisk: true);
            }

            File.Move(temporaryArchivePath, normalizedDestination, overwrite: true);
            var archiveSize = new FileInfo(normalizedDestination).Length;
            progress?.Report(new LibraryBackupProgress(
                "Backup complete",
                sources.Count,
                sources.Count,
                totalBytes,
                totalBytes));

            return new LibraryBackupSummary(
                normalizedDestination,
                createdAtUtc,
                catalogBooks.Count,
                sources.Count,
                totalBytes,
                archiveSize,
                CurrentFormatVersion,
                completeCatalog);
        }
        finally
        {
            if (File.Exists(temporaryArchivePath))
            {
                File.Delete(temporaryArchivePath);
            }

            if (Directory.Exists(temporaryRoot))
            {
                Directory.Delete(temporaryRoot, recursive: true);
            }
        }
    }

    private void CreateDatabaseSnapshot(string destinationPath)
    {
        using var source = GetDatabase().OpenConnection();
        using var destination = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = destinationPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
            }.ToString());
        destination.Open();
        source.BackupDatabase(destination);

        using var checkpoint = destination.CreateCommand();
        checkpoint.CommandText = "PRAGMA journal_mode = DELETE;";
        checkpoint.ExecuteNonQuery();
        EnsureDatabaseIntegrity(destination);
    }

    private List<ArchiveSource> BuildArchiveSources(string databaseSnapshotPath)
    {
        var sources = new List<ArchiveSource>
        {
            new(databaseSnapshotPath, DatabaseArchivePath, CompressionLevel.Optimal),
        };

        AddDirectorySources(_managedLibraryPath, sources);
        AddDirectorySources(_coverCachePath, sources);
        return sources
            .OrderBy(source => source.ArchivePath, StringComparer.Ordinal)
            .ToList();
    }

    private void AddDirectorySources(string rootPath, List<ArchiveSource> sources)
    {
        if (!Directory.Exists(rootPath))
        {
            return;
        }

        EnsurePathContainsNoLinks(rootPath);
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(rootPath);
        while (pendingDirectories.Count > 0)
        {
            var currentDirectory = pendingDirectories.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(currentDirectory))
            {
                if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        "ListenShelf will not include symbolic links or filesystem junctions in a backup.");
                }

                if (Directory.Exists(entry))
                {
                    pendingDirectories.Push(entry);
                }
                else if (File.Exists(entry))
                {
                    sources.Add(new ArchiveSource(
                        Path.GetFullPath(entry),
                        ToDataArchivePath(entry),
                        CompressionLevel.NoCompression));
                }
            }
        }
    }

    private List<string> BuildArchiveDirectories()
    {
        var directories = new List<string>();
        AddDirectoryEntries(_managedLibraryPath, directories);
        AddDirectoryEntries(_coverCachePath, directories);
        return directories
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }

    private void AddDirectoryEntries(string rootPath, List<string> directories)
    {
        if (!Directory.Exists(rootPath))
        {
            return;
        }

        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(rootPath);
        while (pendingDirectories.Count > 0)
        {
            var currentDirectory = pendingDirectories.Pop();
            directories.Add(ToDataArchivePath(currentDirectory));
            foreach (var directory in Directory.EnumerateDirectories(currentDirectory))
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        "ListenShelf will not include symbolic links or filesystem junctions in a backup.");
                }

                pendingDirectories.Push(directory);
            }
        }
    }

    private BackupFileManifest AddFileToArchive(
        ZipArchive archive,
        ArchiveSource source,
        long totalBytes,
        ref long completedBytes,
        ref int completedFiles,
        int totalFiles,
        IProgress<LibraryBackupProgress>? progress)
    {
        var entry = archive.CreateEntry(source.ArchivePath, source.CompressionLevel);
        using var input = new FileStream(
            source.SourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            FileOptions.SequentialScan);
        using var output = entry.Open();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var expectedLength = input.Length;
        var writtenLength = 0L;
        var lastReportedBytes = completedBytes;
        var buffer = new byte[1024 * 1024];
        int bytesRead;
        while ((bytesRead = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            output.Write(buffer, 0, bytesRead);
            hash.AppendData(buffer, 0, bytesRead);
            writtenLength += bytesRead;
            completedBytes += bytesRead;
            if (completedBytes - lastReportedBytes >= ProgressReportIntervalBytes)
            {
                progress?.Report(new LibraryBackupProgress(
                    "Writing backup",
                    completedFiles,
                    totalFiles,
                    completedBytes,
                    totalBytes));
                lastReportedBytes = completedBytes;
            }
        }

        if (writtenLength != expectedLength || input.Length != expectedLength)
        {
            throw new IOException(
                $"{Path.GetFileName(source.SourcePath)} changed while the backup was being created.");
        }

        completedFiles++;
        progress?.Report(new LibraryBackupProgress(
            "Writing backup",
            completedFiles,
            totalFiles,
            completedBytes,
            totalBytes));
        return new BackupFileManifest(
            source.ArchivePath,
            writtenLength,
            Convert.ToHexString(hash.GetHashAndReset()));
    }

    private BackupManifest ReadAndValidateManifest(ZipArchive archive)
    {
        var manifestEntry = archive.GetEntry(ManifestArchivePath)
            ?? throw new InvalidDataException(
                "This file does not contain a ListenShelf backup manifest.");
        if (manifestEntry.Length <= 0 || manifestEntry.Length > MaximumManifestBytes)
        {
            throw new InvalidDataException("The backup manifest has an invalid size.");
        }

        BackupManifest manifest;
        using (var stream = manifestEntry.Open())
        {
            manifest = JsonSerializer.Deserialize<BackupManifest>(stream, JsonOptions)
                ?? throw new InvalidDataException("The backup manifest is empty.");
        }

        if (manifest.Directories is null
            || manifest.Books is null
            || manifest.Files is null)
        {
            throw new InvalidDataException(
                "The backup manifest is missing required collections.");
        }

        if (!string.Equals(manifest.Format, BackupFormatName, StringComparison.Ordinal)
            || manifest.FormatVersion != CurrentFormatVersion)
        {
            throw new NotSupportedException(
                $"This backup uses format version {manifest.FormatVersion}; this ListenShelf build supports version {CurrentFormatVersion}.");
        }

        if (!string.Equals(
                manifest.DatabaseArchivePath,
                DatabaseArchivePath,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("The backup database location is invalid.");
        }

        ValidateArchivePath(manifest.DatabaseArchivePath);
        var filePaths = new HashSet<string>(ArchivePathComparer);
        foreach (var file in manifest.Files)
        {
            ValidateArchivePath(file.Path);
            if (!filePaths.Add(file.Path)
                || file.Length < 0
                || string.IsNullOrWhiteSpace(file.Sha256)
                || file.Sha256.Length != 64
                || !file.Sha256.All(Uri.IsHexDigit))
            {
                throw new InvalidDataException(
                    "The backup manifest contains an invalid or duplicate file entry.");
            }
        }

        if (!filePaths.Contains(DatabaseArchivePath))
        {
            throw new InvalidDataException("The backup does not contain its database snapshot.");
        }

        var directoryPaths = new HashSet<string>(ArchivePathComparer);
        foreach (var directory in manifest.Directories)
        {
            ValidateArchivePath(directory);
            if (!directoryPaths.Add(directory))
            {
                throw new InvalidDataException(
                    "The backup manifest contains a duplicate directory entry.");
            }
        }

        if (directoryPaths.Overlaps(filePaths))
        {
            throw new InvalidDataException(
                "The backup manifest uses the same path for a file and directory.");
        }

        var bookIds = new HashSet<Guid>();
        foreach (var book in manifest.Books)
        {
            if (book.BookId == Guid.Empty
                || !bookIds.Add(book.BookId))
            {
                throw new InvalidDataException(
                    "The backup manifest contains an invalid or duplicate audiobook identifier.");
            }

            ValidateArchivePath(book.AudioArchivePath);
            if (book.CoverArchivePath is not null)
            {
                ValidateArchivePath(book.CoverArchivePath);
            }

            if (manifest.IsComplete
                && (!filePaths.Contains(book.AudioArchivePath)
                    || (book.CoverArchivePath is not null
                        && !filePaths.Contains(book.CoverArchivePath))))
            {
                throw new InvalidDataException(
                    "The backup claims to be complete but is missing a cataloged audiobook or cover.");
            }
        }

        if (manifest.BookCount != manifest.Books.Count)
        {
            throw new InvalidDataException(
                "The backup audiobook count does not match its manifest.");
        }


        if (archive.Entries.Count(entry => string.Equals(
                entry.FullName,
                ManifestArchivePath,
                StringComparison.OrdinalIgnoreCase)) != 1)
        {
            throw new InvalidDataException(
                "The backup must contain exactly one manifest.");
        }

        var allArchivePaths = new HashSet<string>(ArchivePathComparer);
        if (archive.Entries.Any(entry => !allArchivePaths.Add(entry.FullName)))
        {
            throw new InvalidDataException(
                "The backup contains duplicate archive paths.");
        }

        return manifest;
    }

    private void ValidateArchiveContents(
        ZipArchive archive,
        BackupManifest manifest,
        IProgress<LibraryBackupProgress>? progress,
        string? extractRoot)
    {
        var archiveEntries = archive.Entries
            .Where(entry => !string.Equals(
                entry.FullName,
                ManifestArchivePath,
                StringComparison.OrdinalIgnoreCase))
            .ToDictionary(entry => entry.FullName, ArchivePathComparer);
        if (archiveEntries.Count != manifest.Files.Count)
        {
            throw new InvalidDataException(
                "The backup contains files that are missing from its manifest, or manifest files are missing from the archive.");
        }

        if (extractRoot is not null)
        {
            foreach (var directory in manifest.Directories)
            {
                Directory.CreateDirectory(ResolveExtractionPath(extractRoot, directory));
            }
        }

        var totalBytes = manifest.Files.Sum(file => file.Length);
        var completedBytes = 0L;
        var completedFiles = 0;
        var lastReportedBytes = 0L;
        foreach (var file in manifest.Files)
        {
            if (!archiveEntries.TryGetValue(file.Path, out var entry)
                || entry.Length != file.Length)
            {
                throw new InvalidDataException(
                    $"The backup entry {file.Path} is missing or has the wrong size.");
            }

            using var input = entry.Open();
            FileStream? output = null;
            try
            {
                if (extractRoot is not null)
                {
                    var destinationPath = ResolveExtractionPath(extractRoot, file.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                    output = new FileStream(
                        destinationPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize: 1024 * 1024,
                        FileOptions.WriteThrough);
                }

                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var readLength = 0L;
                var buffer = new byte[1024 * 1024];
                int bytesRead;
                while ((bytesRead = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output?.Write(buffer, 0, bytesRead);
                    hash.AppendData(buffer, 0, bytesRead);
                    readLength += bytesRead;
                    completedBytes += bytesRead;
                    if (completedBytes - lastReportedBytes >= ProgressReportIntervalBytes)
                    {
                        progress?.Report(new LibraryBackupProgress(
                            extractRoot is null ? "Validating backup" : "Validating and staging backup",
                            completedFiles,
                            manifest.Files.Count,
                            completedBytes,
                            totalBytes));
                        lastReportedBytes = completedBytes;
                    }
                }

                output?.Flush(flushToDisk: true);
                var actualHash = Convert.ToHexString(hash.GetHashAndReset());
                if (readLength != file.Length
                    || !string.Equals(actualHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"The backup entry {file.Path} failed its integrity check.");
                }
            }
            finally
            {
                output?.Dispose();
            }

            completedFiles++;
            progress?.Report(new LibraryBackupProgress(
                extractRoot is null ? "Validating backup" : "Validating and staging backup",
                completedFiles,
                manifest.Files.Count,
                completedBytes,
                totalBytes));
        }
    }

    private void ValidateAndRebaseDatabase(
        string stagedDatabasePath,
        string stagedDataRoot,
        BackupManifest manifest)
    {
        if (!File.Exists(stagedDatabasePath))
        {
            throw new InvalidDataException("The staged backup database is missing.");
        }

        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = stagedDatabasePath,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false,
            }.ToString());
        connection.Open();
        EnsureDatabaseIntegrity(connection);
        EnsureRequiredSchema(connection);

        using var transaction = connection.BeginTransaction();
        var pathMappings = new List<PathMapping>(manifest.Books.Count);
        foreach (var book in manifest.Books)
        {
            using var readCommand = connection.CreateCommand();
            readCommand.Transaction = transaction;
            readCommand.CommandText =
                "SELECT file_key FROM library_books WHERE book_id = $book_id;";
            readCommand.Parameters.AddWithValue("$book_id", book.BookId.ToString("D"));
            var oldFileKey = readCommand.ExecuteScalar() as string
                ?? throw new InvalidDataException(
                    "The backup database does not contain every audiobook listed in its manifest.");

            var stagedAudioPath = ResolveExtractionPath(
                Path.GetDirectoryName(stagedDataRoot)!,
                book.AudioArchivePath);
            var newAudioPath = ResolveFinalDataPath(book.AudioArchivePath);
            if (manifest.IsComplete && !File.Exists(stagedAudioPath))
            {
                throw new InvalidDataException(
                    $"The staged audiobook for {book.BookId:D} is missing.");
            }

            string? newCoverPath = null;
            if (book.CoverArchivePath is not null)
            {
                var stagedCoverPath = ResolveExtractionPath(
                    Path.GetDirectoryName(stagedDataRoot)!,
                    book.CoverArchivePath);
                if (manifest.IsComplete && !File.Exists(stagedCoverPath))
                {
                    throw new InvalidDataException(
                        $"The staged cover for {book.BookId:D} is missing.");
                }

                newCoverPath = ResolveFinalDataPath(book.CoverArchivePath);
            }

            pathMappings.Add(new PathMapping(
                book.BookId,
                oldFileKey,
                CreatePathKey(newAudioPath),
                newAudioPath,
                newCoverPath));
        }

        if (CountRows(connection, transaction, "library_books") != manifest.BookCount)
        {
            throw new InvalidDataException(
                "The backup database audiobook count does not match its manifest.");
        }

        foreach (var mapping in pathMappings)
        {
            var temporaryKey = $"restore:{mapping.BookId:N}";
            ExecutePathUpdate(
                connection,
                transaction,
                "library_books",
                mapping.OldFileKey,
                temporaryKey,
                mapping.NewFilePath,
                mapping.NewCoverPath,
                mapping.BookId);
            ExecuteRelatedPathUpdate(
                connection,
                transaction,
                "playback_progress",
                mapping.OldFileKey,
                temporaryKey,
                mapping.NewFilePath);
            ExecuteRelatedPathUpdate(
                connection,
                transaction,
                "playback_bookmarks",
                mapping.OldFileKey,
                temporaryKey,
                mapping.NewFilePath);
        }

        foreach (var mapping in pathMappings)
        {
            var temporaryKey = $"restore:{mapping.BookId:N}";
            ExecutePathUpdate(
                connection,
                transaction,
                "library_books",
                temporaryKey,
                mapping.NewFileKey,
                mapping.NewFilePath,
                mapping.NewCoverPath,
                mapping.BookId);
            ExecuteRelatedPathUpdate(
                connection,
                transaction,
                "playback_progress",
                temporaryKey,
                mapping.NewFileKey,
                mapping.NewFilePath);
            ExecuteRelatedPathUpdate(
                connection,
                transaction,
                "playback_bookmarks",
                temporaryKey,
                mapping.NewFileKey,
                mapping.NewFilePath);
            ExecutePendingRemovalPathUpdate(connection, transaction, mapping);
        }

        transaction.Commit();
        EnsureDatabaseIntegrity(connection);
    }

    private static void ValidateDatabaseEntry(
        ZipArchive archive,
        BackupManifest manifest)
    {
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "ListenShelf.Backup",
            $"inspect-{Guid.NewGuid():N}");
        var temporaryDatabasePath = Path.Combine(temporaryRoot, "listenshelf.db");
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            var databaseEntry = archive.Entries.SingleOrDefault(entry =>
                ArchivePathComparer.Equals(
                    entry.FullName,
                    manifest.DatabaseArchivePath))
                ?? throw new InvalidDataException(
                    "The backup database entry is missing.");
            using (var source = databaseEntry.Open())
            using (var destination = new FileStream(
                       temporaryDatabasePath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                source.CopyTo(destination);
                destination.Flush(flushToDisk: true);
            }

            using (var connection = OpenDatabase(
                       temporaryDatabasePath,
                       SqliteOpenMode.ReadWrite))
            {
                EnsureDatabaseIntegrity(connection);
                EnsureRequiredSchema(connection);
            }

            _ = new ListenShelfDatabase(
                temporaryDatabasePath,
                createMigrationSafetyCopy: false);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(temporaryRoot))
            {
                Directory.Delete(temporaryRoot, recursive: true);
            }
        }
    }

    private static void ExecutePathUpdate(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        string oldFileKey,
        string newFileKey,
        string newFilePath,
        string? newCoverPath,
        Guid bookId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            UPDATE {tableName}
            SET file_key = $new_file_key,
                file_path = $new_file_path,
                cover_path = $new_cover_path
            WHERE book_id = $book_id
              AND file_key = $old_file_key;
            """;
        command.Parameters.AddWithValue("$new_file_key", newFileKey);
        command.Parameters.AddWithValue("$new_file_path", newFilePath);
        command.Parameters.AddWithValue(
            "$new_cover_path",
            (object?)newCoverPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$book_id", bookId.ToString("D"));
        command.Parameters.AddWithValue("$old_file_key", oldFileKey);
        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidDataException(
                "The backup database changed while its paths were being restored.");
        }
    }

    private static void ExecuteRelatedPathUpdate(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        string oldFileKey,
        string newFileKey,
        string newFilePath)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            UPDATE {tableName}
            SET file_key = $new_file_key,
                file_path = $new_file_path
            WHERE file_key = $old_file_key;
            """;
        command.Parameters.AddWithValue("$new_file_key", newFileKey);
        command.Parameters.AddWithValue("$new_file_path", newFilePath);
        command.Parameters.AddWithValue("$old_file_key", oldFileKey);
        command.ExecuteNonQuery();
    }

    private static void ExecutePendingRemovalPathUpdate(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PathMapping mapping)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE pending_library_removals
            SET file_path = $file_path,
                cover_path = $cover_path
            WHERE book_id = $book_id;
            """;
        command.Parameters.AddWithValue("$file_path", mapping.NewFilePath);
        command.Parameters.AddWithValue(
            "$cover_path",
            (object?)mapping.NewCoverPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$book_id", mapping.BookId.ToString("D"));
        command.ExecuteNonQuery();
    }

    private static int CountRows(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private void ValidateLiveDatabase()
    {
        var liveDatabase = new ListenShelfDatabase(
            Path.Combine(DataRootPath, "listenshelf.db"),
            createMigrationSafetyCopy: false);
        using var connection = liveDatabase.OpenConnection();
        EnsureDatabaseIntegrity(connection);
        EnsureRequiredSchema(connection);
    }

    private ListenShelfDatabase GetDatabase() =>
        _database ?? throw new InvalidOperationException(
            "A healthy live database is required for this backup operation.");

    private void EnsureNormalLibraryIsAvailable() => _ = GetDatabase();

    private static void EnsureDatabaseIntegrity(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        var result = command.ExecuteScalar() as string;
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"The backup database failed SQLite's integrity check: {result ?? "unknown error"}.");
        }
    }

    private static void EnsureRequiredSchema(SqliteConnection connection)
    {
        var requiredTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "app_settings",
            "library_books",
            "pending_library_removals",
            "playback_bookmarks",
            "playback_progress",
        };
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name FROM sqlite_master WHERE type = 'table';";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            requiredTables.Remove(reader.GetString(0));
        }

        if (requiredTables.Count > 0)
        {
            throw new InvalidDataException(
                "The backup database is missing required ListenShelf tables.");
        }
    }

    private List<CatalogBookPath> ReadCatalogBooks(string databasePath)
    {
        using var connection = OpenDatabase(databasePath, SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT book_id, file_path, cover_path FROM library_books ORDER BY book_id;";
        using var reader = command.ExecuteReader();
        var books = new List<CatalogBookPath>();
        while (reader.Read())
        {
            if (!Guid.TryParse(reader.GetString(0), out var bookId))
            {
                throw new InvalidDataException(
                    "The library database contains an invalid audiobook identifier.");
            }

            books.Add(new CatalogBookPath(
                bookId,
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return books;
    }

    private static int ReadPendingRemovalCount(string databasePath)
    {
        using var connection = OpenDatabase(databasePath, SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pending_library_removals;";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static SqliteConnection OpenDatabase(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = mode,
                Pooling = false,
            }.ToString());
        connection.Open();
        return connection;
    }

    private LibraryBackupSummary CreateSummary(
        string backupPath,
        BackupManifest manifest,
        long archiveSize) =>
        new(
            backupPath,
            manifest.CreatedAtUtc,
            manifest.BookCount,
            manifest.Files.Count,
            manifest.Files.Sum(file => file.Length),
            archiveSize,
            manifest.FormatVersion,
            manifest.IsComplete);

    private string NormalizeDestinationPath(string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException(
                "A backup destination is required.",
                nameof(destinationPath));
        }

        var normalizedPath = Path.GetFullPath(destinationPath);
        return normalizedPath.EndsWith(BackupFileExtension, _pathComparison)
            ? normalizedPath
            : normalizedPath + BackupFileExtension;
    }

    private string NormalizeExistingBackupPath(string backupPath)
    {
        if (string.IsNullOrWhiteSpace(backupPath))
        {
            throw new ArgumentException("A backup path is required.", nameof(backupPath));
        }

        var normalizedPath = Path.GetFullPath(backupPath);
        if (!File.Exists(normalizedPath))
        {
            throw new FileNotFoundException(
                "The selected ListenShelf backup could not be found.",
                normalizedPath);
        }

        return normalizedPath;
    }

    private string ToDataArchivePath(string path)
    {
        var normalizedPath = Path.GetFullPath(path);
        EnsurePathIsInsideDataRoot(normalizedPath, "backup item");
        var relativePath = Path.GetRelativePath(DataRootPath, normalizedPath);
        return $"data/{relativePath.Replace(Path.DirectorySeparatorChar, '/')}";
    }

    private string ToDataArchivePath(string path, string? incompleteFallbackPath)
    {
        try
        {
            return ToDataArchivePath(path);
        }
        catch (InvalidOperationException) when (incompleteFallbackPath is not null)
        {
            ValidateArchivePath(incompleteFallbackPath);
            return incompleteFallbackPath;
        }
    }

    private static string GetSafeArchiveFileName(string path, string fallbackName)
    {
        try
        {
            var fileName = Path.GetFileName(path);
            return string.IsNullOrWhiteSpace(fileName)
                   || fileName is "." or ".."
                   || fileName.Contains('/')
                   || fileName.Contains('\\')
                ? fallbackName
                : fileName;
        }
        catch (Exception exception) when (exception is ArgumentException
                                           or NotSupportedException
                                           or PathTooLongException)
        {
            return fallbackName;
        }
    }

    private string ResolveFinalDataPath(string archivePath)
    {
        ValidateArchivePath(archivePath);
        var relativePath = archivePath["data/".Length..]
            .Replace('/', Path.DirectorySeparatorChar);
        var resolvedPath = Path.GetFullPath(Path.Combine(DataRootPath, relativePath));
        EnsurePathIsInsideDataRoot(resolvedPath, "restored item");
        return resolvedPath;
    }

    private static string ResolveExtractionPath(string rootPath, string archivePath)
    {
        ValidateArchivePath(archivePath);
        var relativePath = archivePath.Replace('/', Path.DirectorySeparatorChar);
        var normalizedRoot = Path.GetFullPath(rootPath);
        var resolvedPath = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        var relativeToRoot = Path.GetRelativePath(normalizedRoot, resolvedPath);
        if (relativeToRoot == "."
            || Path.IsPathRooted(relativeToRoot)
            || relativeToRoot == ".."
            || relativeToRoot.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The backup contains a path outside its staging directory.");
        }

        return resolvedPath;
    }

    private static void ValidateArchivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path.Contains('\\')
            || path.StartsWith('/')
            || !path.StartsWith("data/", StringComparison.Ordinal)
            || path.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new InvalidDataException(
                "The backup contains an unsafe archive path.");
        }
    }

    private void EnsurePathIsInsideDataRoot(string path, string description)
    {
        var relativePath = Path.GetRelativePath(DataRootPath, Path.GetFullPath(path));
        if (relativePath == "."
            || Path.IsPathRooted(relativePath)
            || relativePath.Equals("..", _pathComparison)
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", _pathComparison)
            || relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", _pathComparison))
        {
            throw new InvalidOperationException(
                $"The {description} must be inside ListenShelf's data directory.");
        }
    }

    private void EnsurePathIsOutsideDataRoot(string path, string description)
    {
        var relativePath = Path.GetRelativePath(DataRootPath, Path.GetFullPath(path));
        if (relativePath == "."
            || (!Path.IsPathRooted(relativePath)
                && !relativePath.Equals("..", _pathComparison)
                && !relativePath.StartsWith(
                    $"..{Path.DirectorySeparatorChar}",
                    _pathComparison)
                && !relativePath.StartsWith(
                    $"..{Path.AltDirectorySeparatorChar}",
                    _pathComparison)))
        {
            throw new InvalidOperationException(
                $"{description} must be outside ListenShelf's live data directory.");
        }
    }

    private void EnsurePathContainsNoLinks(string path)
    {
        var currentPath = Path.GetFullPath(path);
        while (true)
        {
            if ((File.GetAttributes(currentPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    "ListenShelf will not back up symbolic links or filesystem junctions.");
            }

            if (PathsEqual(currentPath, DataRootPath))
            {
                return;
            }

            currentPath = Path.GetDirectoryName(currentPath)
                ?? throw new InvalidOperationException(
                    "A backup source path could not be validated.");
        }
    }

    private string CreateSafetyBackupPath(string importedBackupPath)
    {
        var directory = Path.GetDirectoryName(importedBackupPath)
            ?? throw new InvalidOperationException(
                "The selected backup needs a parent directory.");
        var timestamp = DateTimeOffset.Now.ToString(
            "yyyy-MM-dd HH-mm-ss",
            CultureInfo.InvariantCulture);
        var basePath = Path.Combine(
            directory,
            $"ListenShelf before restore {timestamp}{BackupFileExtension}");
        if (!File.Exists(basePath))
        {
            return basePath;
        }

        return Path.Combine(
            directory,
            $"ListenShelf before restore {timestamp} {Guid.NewGuid():N}{BackupFileExtension}");
    }

    private static string CreatePreservedDataPath(string dataRootParent)
    {
        var timestamp = DateTimeOffset.Now.ToString(
            "yyyy-MM-dd HH-mm-ss",
            CultureInfo.InvariantCulture);
        var preferredPath = Path.Combine(
            dataRootParent,
            $"ListenShelf Recovered Data {timestamp}");
        var path = preferredPath;
        while (Directory.Exists(path) || File.Exists(path))
        {
            path = $"{preferredPath} {Guid.NewGuid():N}";
        }

        return path;
    }

    private bool PathsEqual(string firstPath, string secondPath) =>
        string.Equals(
            Path.GetFullPath(firstPath),
            Path.GetFullPath(secondPath),
            _pathComparison);

    private static string CreatePathKey(string path)
    {
        var normalizedPath = Path.GetFullPath(path);
        return OperatingSystem.IsWindows()
            ? normalizedPath.ToUpperInvariant()
            : normalizedPath;
    }

    private static string GetApplicationVersion() =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "unknown";

    private static bool IsFilesystemException(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException;

    private sealed record ArchiveSource(
        string SourcePath,
        string ArchivePath,
        CompressionLevel CompressionLevel)
    {
        public long Length => new FileInfo(SourcePath).Length;
    }

    private sealed record CatalogBookPath(
        Guid BookId,
        string FilePath,
        string? CoverPath);

    private sealed record BackupManifest(
        string Format,
        int FormatVersion,
        DateTimeOffset CreatedAtUtc,
        string ApplicationVersion,
        bool IsComplete,
        int BookCount,
        string DatabaseArchivePath,
        IReadOnlyList<string> Directories,
        IReadOnlyList<BackupBookManifest> Books,
        IReadOnlyList<BackupFileManifest> Files);

    private sealed record BackupBookManifest(
        Guid BookId,
        string AudioArchivePath,
        string? CoverArchivePath);

    private sealed record BackupFileManifest(
        string Path,
        long Length,
        string Sha256);

    private sealed record PathMapping(
        Guid BookId,
        string OldFileKey,
        string NewFileKey,
        string NewFilePath,
        string? NewCoverPath);
}
