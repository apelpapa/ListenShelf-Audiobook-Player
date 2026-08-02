using ListenShelf.Application.Library;
using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Infrastructure.Library;

public sealed class SqliteManagedLibraryIntegrityChecker : IManagedLibraryIntegrityChecker
{
    private const string RemovalStagingDirectoryName = ".removing";
    private static readonly TimeSpan DefaultStaleImportAge = TimeSpan.FromHours(24);
    private readonly ListenShelfDatabase _database;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _staleImportAge;
    private readonly StringComparer _pathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public SqliteManagedLibraryIntegrityChecker(
        ListenShelfDatabase database,
        string managedLibraryPath,
        TimeProvider? timeProvider = null,
        TimeSpan? staleImportAge = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));

        if (string.IsNullOrWhiteSpace(managedLibraryPath))
        {
            throw new ArgumentException(
                "A managed-library path is required.",
                nameof(managedLibraryPath));
        }

        ManagedLibraryPath = Path.GetFullPath(managedLibraryPath);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _staleImportAge = staleImportAge ?? DefaultStaleImportAge;
        if (_staleImportAge <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(staleImportAge),
                "The stale-import age must be greater than zero.");
        }
    }

    public string ManagedLibraryPath { get; }

    public ManagedLibraryIntegrityReport Check()
    {
        var checkedAtUtc = _timeProvider.GetUtcNow();
        var catalogState = ReadCatalogState();
        var issues = new List<ManagedLibraryIntegrityIssue>();
        var expectedDirectories = new Dictionary<string, CatalogBookReference>(_pathComparer);
        var referencedFileCount = 0;

        foreach (var book in catalogState.Books)
        {
            var expectedDirectory = Path.GetFullPath(
                Path.Combine(ManagedLibraryPath, book.BookId.ToString("N")));
            expectedDirectories[expectedDirectory] = book;

            string actualDirectory;
            try
            {
                actualDirectory = Path.GetDirectoryName(Path.GetFullPath(book.FilePath))
                    ?? string.Empty;
            }
            catch (Exception exception) when (exception is ArgumentException
                                               or NotSupportedException
                                               or PathTooLongException)
            {
                issues.Add(new ManagedLibraryIntegrityIssue(
                    ManagedLibraryIntegrityIssueKind.CatalogPathOutsideManagedStorage,
                    book.FilePath,
                    "The catalog contains an invalid managed audiobook path.",
                    book.BookId));
                continue;
            }

            if (!_pathComparer.Equals(expectedDirectory, actualDirectory))
            {
                issues.Add(new ManagedLibraryIntegrityIssue(
                    ManagedLibraryIntegrityIssueKind.CatalogPathOutsideManagedStorage,
                    book.FilePath,
                    "The cataloged audiobook path is outside its expected ListenShelf-managed directory.",
                    book.BookId));
                continue;
            }

            if (!Directory.Exists(expectedDirectory))
            {
                issues.Add(new ManagedLibraryIntegrityIssue(
                    ManagedLibraryIntegrityIssueKind.MissingManagedDirectory,
                    expectedDirectory,
                    "The catalog references a managed book directory that is missing.",
                    book.BookId));
                continue;
            }

            if (!File.Exists(book.FilePath))
            {
                issues.Add(new ManagedLibraryIntegrityIssue(
                    ManagedLibraryIntegrityIssueKind.MissingManagedFile,
                    book.FilePath,
                    "The catalog references a managed audiobook file that is missing.",
                    book.BookId));
                continue;
            }

            referencedFileCount++;
        }

        if (Directory.Exists(ManagedLibraryPath))
        {
            AuditManagedLibraryRoot(
                catalogState,
                expectedDirectories,
                checkedAtUtc,
                issues);
        }

        return new ManagedLibraryIntegrityReport(
            checkedAtUtc,
            catalogState.Books.Count,
            referencedFileCount,
            issues
                .OrderBy(issue => issue.Kind)
                .ThenBy(issue => issue.Path, _pathComparer)
                .ToArray());
    }

    private void AuditManagedLibraryRoot(
        CatalogState catalogState,
        IReadOnlyDictionary<string, CatalogBookReference> expectedDirectories,
        DateTimeOffset checkedAtUtc,
        List<ManagedLibraryIntegrityIssue> issues)
    {
        IReadOnlyList<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(ManagedLibraryPath).ToArray();
        }
        catch (Exception exception) when (IsFilesystemInspectionException(exception))
        {
            AddUnreadablePathIssue(ManagedLibraryPath, issues);
            return;
        }

        foreach (var entry in entries)
        {
            if (Directory.Exists(entry))
            {
                if (string.Equals(
                        Path.GetFileName(entry),
                        RemovalStagingDirectoryName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    AuditRemovalStaging(
                        entry,
                        catalogState.PendingRemovalBookIds,
                        checkedAtUtc,
                        issues);
                }
                else if (expectedDirectories.TryGetValue(
                             Path.GetFullPath(entry),
                             out var catalogBook))
                {
                    AuditCatalogBookDirectory(entry, catalogBook, checkedAtUtc, issues);
                }
                else
                {
                    AddUnreferencedDirectoryIssue(entry, checkedAtUtc, issues);
                }

                continue;
            }

            if (File.Exists(entry))
            {
                AddUnexpectedFileIssue(entry, checkedAtUtc, issues);
            }
        }
    }

    private void AuditCatalogBookDirectory(
        string directoryPath,
        CatalogBookReference catalogBook,
        DateTimeOffset checkedAtUtc,
        List<ManagedLibraryIntegrityIssue> issues)
    {
        IReadOnlyList<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(directoryPath).ToArray();
        }
        catch (Exception exception) when (IsFilesystemInspectionException(exception))
        {
            AddUnreadablePathIssue(directoryPath, issues, catalogBook.BookId);
            return;
        }

        foreach (var entry in entries)
        {
            if (File.Exists(entry))
            {
                if (!_pathComparer.Equals(Path.GetFullPath(entry), Path.GetFullPath(catalogBook.FilePath)))
                {
                    AddUnexpectedFileIssue(entry, checkedAtUtc, issues, catalogBook.BookId);
                }

                continue;
            }

            if (Directory.Exists(entry))
            {
                AddUnreferencedDirectoryIssue(entry, checkedAtUtc, issues, catalogBook.BookId);
            }
        }
    }

    private void AuditRemovalStaging(
        string stagingRoot,
        IReadOnlySet<Guid> pendingRemovalBookIds,
        DateTimeOffset checkedAtUtc,
        List<ManagedLibraryIntegrityIssue> issues)
    {
        IReadOnlyList<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(stagingRoot).ToArray();
        }
        catch (Exception exception) when (IsFilesystemInspectionException(exception))
        {
            AddUnreadablePathIssue(stagingRoot, issues);
            return;
        }

        foreach (var entry in entries)
        {
            if (Directory.Exists(entry)
                && Guid.TryParseExact(Path.GetFileName(entry), "N", out var bookId)
                && pendingRemovalBookIds.Contains(bookId))
            {
                issues.Add(new ManagedLibraryIntegrityIssue(
                    ManagedLibraryIntegrityIssueKind.PendingRemovalCleanup,
                    Path.GetFullPath(entry),
                    "A confirmed book removal is still waiting for filesystem cleanup.",
                    bookId));
                continue;
            }

            if (Directory.Exists(entry))
            {
                AddUnreferencedDirectoryIssue(entry, checkedAtUtc, issues);
            }
            else if (File.Exists(entry))
            {
                AddUnexpectedFileIssue(entry, checkedAtUtc, issues);
            }
        }
    }

    private void AddUnreferencedDirectoryIssue(
        string directoryPath,
        DateTimeOffset checkedAtUtc,
        List<ManagedLibraryIntegrityIssue> issues,
        Guid? bookId = null)
    {
        issues.Add(new ManagedLibraryIntegrityIssue(
            ManagedLibraryIntegrityIssueKind.UnreferencedDirectory,
            Path.GetFullPath(directoryPath),
            "This directory is not referenced by the ListenShelf catalog or removal journal.",
            bookId));

        AuditUnreferencedDirectoryContents(directoryPath, checkedAtUtc, issues, bookId);
    }

    private void AuditUnreferencedDirectoryContents(
        string directoryPath,
        DateTimeOffset checkedAtUtc,
        List<ManagedLibraryIntegrityIssue> issues,
        Guid? bookId)
    {
        IReadOnlyList<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(directoryPath).ToArray();
        }
        catch (Exception exception) when (IsFilesystemInspectionException(exception))
        {
            AddUnreadablePathIssue(directoryPath, issues, bookId);
            return;
        }

        foreach (var entry in entries)
        {
            if (File.Exists(entry))
            {
                AddUnexpectedFileIssue(entry, checkedAtUtc, issues, bookId);
            }
            else if (Directory.Exists(entry))
            {
                issues.Add(new ManagedLibraryIntegrityIssue(
                    ManagedLibraryIntegrityIssueKind.UnreferencedDirectory,
                    Path.GetFullPath(entry),
                    "This nested directory is not referenced by the ListenShelf catalog or removal journal.",
                    bookId));
            }
        }
    }

    private void AddUnexpectedFileIssue(
        string filePath,
        DateTimeOffset checkedAtUtc,
        List<ManagedLibraryIntegrityIssue> issues,
        Guid? bookId = null)
    {
        if (filePath.EndsWith(".importing", StringComparison.OrdinalIgnoreCase))
        {
            if (IsStaleImportFile(filePath, checkedAtUtc))
            {
                AddStaleImportIssue(filePath, issues, bookId);
            }

            return;
        }

        issues.Add(new ManagedLibraryIntegrityIssue(
            ManagedLibraryIntegrityIssueKind.UnreferencedFile,
            Path.GetFullPath(filePath),
            "This file is not referenced by the ListenShelf catalog or removal journal.",
            bookId));
    }

    private bool IsStaleImportFile(string filePath, DateTimeOffset checkedAtUtc)
    {
        try
        {
            var lastWriteUtc = new DateTimeOffset(File.GetLastWriteTimeUtc(filePath), TimeSpan.Zero);
            return checkedAtUtc - lastWriteUtc >= _staleImportAge;
        }
        catch (Exception exception) when (IsFilesystemInspectionException(exception))
        {
            return false;
        }
    }

    private static void AddStaleImportIssue(
        string filePath,
        List<ManagedLibraryIntegrityIssue> issues,
        Guid? bookId)
    {
        issues.Add(new ManagedLibraryIntegrityIssue(
            ManagedLibraryIntegrityIssueKind.StaleImportFile,
            Path.GetFullPath(filePath),
            "This unfinished import has not changed for at least 24 hours.",
            bookId));
    }

    private static void AddUnreadablePathIssue(
        string path,
        List<ManagedLibraryIntegrityIssue> issues,
        Guid? bookId = null)
    {
        issues.Add(new ManagedLibraryIntegrityIssue(
            ManagedLibraryIntegrityIssueKind.UnreadablePath,
            Path.GetFullPath(path),
            "ListenShelf could not inspect this managed-storage path.",
            bookId));
    }

    private CatalogState ReadCatalogState()
    {
        using var connection = _database.OpenConnection();

        var pendingRemovalBookIds = new HashSet<Guid>();
        using (var pendingCommand = connection.CreateCommand())
        {
            pendingCommand.CommandText = "SELECT book_id FROM pending_library_removals;";
            using var reader = pendingCommand.ExecuteReader();
            while (reader.Read())
            {
                if (Guid.TryParse(reader.GetString(0), out var bookId))
                {
                    pendingRemovalBookIds.Add(bookId);
                }
            }
        }

        var books = new List<CatalogBookReference>();
        using (var bookCommand = connection.CreateCommand())
        {
            bookCommand.CommandText =
                """
                SELECT books.book_id, books.file_path
                FROM library_books AS books
                WHERE books.storage_mode = 'Managed'
                  AND NOT EXISTS (
                      SELECT 1
                      FROM pending_library_removals AS removals
                      WHERE removals.book_id = books.book_id);
                """;
            using var reader = bookCommand.ExecuteReader();
            while (reader.Read())
            {
                if (Guid.TryParse(reader.GetString(0), out var bookId))
                {
                    books.Add(new CatalogBookReference(bookId, reader.GetString(1)));
                }
            }
        }

        return new CatalogState(books, pendingRemovalBookIds);
    }

    private static bool IsFilesystemInspectionException(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException;

    private sealed record CatalogBookReference(Guid BookId, string FilePath);

    private sealed record CatalogState(
        IReadOnlyList<CatalogBookReference> Books,
        IReadOnlySet<Guid> PendingRemovalBookIds);
}
