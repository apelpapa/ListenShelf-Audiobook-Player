using System.Globalization;
using System.Security.Cryptography;
using ListenShelf.Application.Library;
using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Infrastructure.Library;

public sealed class SqliteManagedLibraryMaintenance : IManagedLibraryMaintenance
{
    private readonly ListenShelfDatabase _database;
    private readonly IManagedLibraryIntegrityChecker _integrityChecker;
    private readonly StringComparison _pathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public SqliteManagedLibraryMaintenance(
        ListenShelfDatabase database,
        IManagedLibraryIntegrityChecker integrityChecker)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _integrityChecker = integrityChecker
            ?? throw new ArgumentNullException(nameof(integrityChecker));
        ManagedLibraryPath = Path.GetFullPath(_integrityChecker.ManagedLibraryPath);
    }

    public string ManagedLibraryPath { get; }

    public ManagedLibraryRecoveryResult RecoverAudiobook(string orphanedFilePath)
    {
        var normalizedPath = ValidateManagedPath(orphanedFilePath);
        _ = FindCurrentIssue(
            normalizedPath,
            ManagedLibraryIntegrityIssueKind.UnreferencedFile);

        if (!File.Exists(normalizedPath))
        {
            throw new FileNotFoundException(
                "The orphaned audiobook is no longer available.",
                normalizedPath);
        }

        if (!AudiobookFileFormats.IsSupported(normalizedPath))
        {
            throw new NotSupportedException(
                "Only orphaned M4B, M4A, and MP3 files can be recovered into the catalog.");
        }

        EnsurePathContainsNoLinks(normalizedPath);
        var sourceFile = new FileInfo(normalizedPath);

        if (TryGetReusableBookId(sourceFile.DirectoryName, out var reusableBookId))
        {
            var recoveredBook = CreateRecoveredBook(reusableBookId, sourceFile);
            InsertRecoveredBook(recoveredBook);
            return new ManagedLibraryRecoveryResult(
                recoveredBook,
                OrphanCleanupPending: false);
        }

        return CopyIntoRecoveredBook(sourceFile);
    }

    public ManagedLibraryCleanupResult CleanUp(string orphanedPath)
    {
        var normalizedPath = ValidateManagedPath(orphanedPath);
        _ = FindCurrentIssue(
            normalizedPath,
            ManagedLibraryIntegrityIssueKind.UnreferencedFile,
            ManagedLibraryIntegrityIssueKind.UnreferencedDirectory,
            ManagedLibraryIntegrityIssueKind.StaleImportFile);

        var isDirectory = Directory.Exists(normalizedPath);
        if (!isDirectory && !File.Exists(normalizedPath))
        {
            throw new FileNotFoundException(
                "The selected orphaned item is no longer available.",
                normalizedPath);
        }

        EnsureTreeContainsNoLinks(normalizedPath, isDirectory);

        if (isDirectory)
        {
            Directory.Delete(normalizedPath, recursive: true);
            TryDeleteEmptyOrphanParents(Path.GetDirectoryName(normalizedPath));
        }
        else
        {
            File.Delete(normalizedPath);
            TryDeleteEmptyOrphanParents(Path.GetDirectoryName(normalizedPath));
        }

        return new ManagedLibraryCleanupResult(normalizedPath, isDirectory);
    }

    private ManagedLibraryRecoveryResult CopyIntoRecoveredBook(FileInfo sourceFile)
    {
        var bookId = Guid.NewGuid();
        var bookDirectory = Path.Combine(ManagedLibraryPath, bookId.ToString("N"));
        var destinationPath = Path.Combine(bookDirectory, sourceFile.Name);
        var temporaryPath = destinationPath + ".importing";

        Directory.CreateDirectory(bookDirectory);

        try
        {
            CopyAndVerify(sourceFile, temporaryPath);
            File.Move(temporaryPath, destinationPath);
            File.SetLastWriteTimeUtc(destinationPath, sourceFile.LastWriteTimeUtc);

            var recoveredFile = new FileInfo(destinationPath);
            var recoveredBook = CreateRecoveredBook(bookId, recoveredFile);
            try
            {
                InsertRecoveredBook(recoveredBook);
            }
            catch
            {
                File.Delete(destinationPath);
                throw;
            }

            var cleanupPending = !TryDeleteRecoveredSource(sourceFile.FullName);
            return new ManagedLibraryRecoveryResult(recoveredBook, cleanupPending);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            if (Directory.Exists(bookDirectory)
                && !Directory.EnumerateFileSystemEntries(bookDirectory).Any())
            {
                Directory.Delete(bookDirectory);
            }
        }
    }

    private LibraryBook CreateRecoveredBook(Guid bookId, FileInfo file) =>
        new(
            bookId,
            AudiobookMetadata.FromFileName(Path.GetFileNameWithoutExtension(file.Name)),
            file.FullName,
            file.Length,
            DateTimeOffset.UtcNow);

    private void InsertRecoveredBook(LibraryBook book)
    {
        using var connection = _database.OpenConnection();
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
                added_utc)
            VALUES (
                $book_id,
                $title,
                $file_path,
                $file_key,
                'Managed',
                NULL,
                NULL,
                $file_size_bytes,
                $added_utc);
            """;
        command.Parameters.AddWithValue("$book_id", book.Id.ToString("D"));
        command.Parameters.AddWithValue("$title", book.Title);
        command.Parameters.AddWithValue("$file_path", Path.GetFullPath(book.FilePath));
        command.Parameters.AddWithValue("$file_key", CreatePathKey(book.FilePath));
        command.Parameters.AddWithValue("$file_size_bytes", book.FileSizeBytes);
        command.Parameters.AddWithValue(
            "$added_utc",
            book.AddedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private ManagedLibraryIntegrityIssue FindCurrentIssue(
        string normalizedPath,
        params ManagedLibraryIntegrityIssueKind[] allowedKinds)
    {
        var issue = _integrityChecker.Check().Issues.FirstOrDefault(candidate =>
            PathsEqual(candidate.Path, normalizedPath)
            && allowedKinds.Contains(candidate.Kind));

        return issue ?? throw new InvalidOperationException(
            "This item is no longer reported as an orphaned managed-storage item. Run the storage check again.");
    }

    private string ValidateManagedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A managed-storage path is required.", nameof(path));
        }

        var normalizedPath = Path.GetFullPath(path);
        var relativePath = Path.GetRelativePath(ManagedLibraryPath, normalizedPath);
        if (relativePath == "."
            || Path.IsPathRooted(relativePath)
            || relativePath.Equals("..", _pathComparison)
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", _pathComparison)
            || relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", _pathComparison))
        {
            throw new InvalidOperationException(
                "ListenShelf will only maintain a specific item inside its managed library.");
        }

        return normalizedPath;
    }

    private bool TryGetReusableBookId(string? directoryPath, out Guid bookId)
    {
        bookId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return false;
        }

        var normalizedDirectory = Path.GetFullPath(directoryPath);
        var parentDirectory = Directory.GetParent(normalizedDirectory)?.FullName;
        if (parentDirectory is null
            || !PathsEqual(parentDirectory, ManagedLibraryPath)
            || !Guid.TryParseExact(Path.GetFileName(normalizedDirectory), "N", out bookId))
        {
            bookId = Guid.Empty;
            return false;
        }

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS (
                SELECT 1
                FROM library_books
                WHERE book_id = $book_id
                UNION ALL
                SELECT 1
                FROM pending_library_removals
                WHERE book_id = $book_id);
            """;
        command.Parameters.AddWithValue("$book_id", bookId.ToString("D"));
        if (Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
        {
            return true;
        }

        bookId = Guid.Empty;
        return false;
    }

    private bool TryDeleteRecoveredSource(string sourcePath)
    {
        try
        {
            File.Delete(sourcePath);
            TryDeleteEmptyOrphanParents(Path.GetDirectoryName(sourcePath));
            return true;
        }
        catch (Exception exception) when (IsFilesystemException(exception))
        {
            return false;
        }
    }

    private void TryDeleteEmptyOrphanParents(string? directoryPath)
    {
        try
        {
            while (!string.IsNullOrWhiteSpace(directoryPath)
                   && !PathsEqual(directoryPath, ManagedLibraryPath))
            {
                if (!Directory.Exists(directoryPath)
                    || Directory.EnumerateFileSystemEntries(directoryPath).Any())
                {
                    return;
                }

                var parent = Directory.GetParent(directoryPath)?.FullName;
                Directory.Delete(directoryPath);
                directoryPath = parent;
            }
        }
        catch (Exception exception) when (IsFilesystemException(exception))
        {
            // The selected orphan is already handled. Empty parent folders are harmless.
        }
    }

    private void EnsurePathContainsNoLinks(string path)
    {
        var currentPath = path;
        while (true)
        {
            if ((File.GetAttributes(currentPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    "ListenShelf will not recover or delete symbolic links or filesystem junctions.");
            }

            if (PathsEqual(currentPath, ManagedLibraryPath))
            {
                return;
            }

            currentPath = Path.GetDirectoryName(currentPath)
                ?? throw new InvalidOperationException(
                    "The selected managed-storage path could not be validated.");
        }
    }

    private void EnsureTreeContainsNoLinks(string path, bool isDirectory)
    {
        EnsurePathContainsNoLinks(path);
        if (!isDirectory)
        {
            return;
        }

        var pendingPaths = new Stack<string>();
        pendingPaths.Push(path);
        while (pendingPaths.Count > 0)
        {
            var currentPath = pendingPaths.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(currentPath))
            {
                if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        "ListenShelf will not recursively delete a folder containing symbolic links or filesystem junctions.");
                }

                if (Directory.Exists(entry))
                {
                    pendingPaths.Push(entry);
                }
            }
        }
    }

    private static void CopyAndVerify(FileInfo sourceFile, string temporaryPath)
    {
        byte[] sourceHash;

        using (var source = new FileStream(
                   sourceFile.FullName,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.Read))
        using (var destination = new FileStream(
                   temporaryPath,
                   FileMode.CreateNew,
                   FileAccess.Write,
                   FileShare.None))
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[1024 * 1024];
            int bytesRead;

            while ((bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                destination.Write(buffer, 0, bytesRead);
                hash.AppendData(buffer, 0, bytesRead);
            }

            destination.Flush(flushToDisk: true);
            sourceHash = hash.GetHashAndReset();
        }

        var copiedLength = new FileInfo(temporaryPath).Length;
        if (copiedLength != sourceFile.Length)
        {
            throw new IOException(
                $"The recovered copy was incomplete: expected {sourceFile.Length} bytes but copied {copiedLength} bytes.");
        }

        using var copiedFile = File.OpenRead(temporaryPath);
        var copiedHash = SHA256.HashData(copiedFile);
        if (!CryptographicOperations.FixedTimeEquals(sourceHash, copiedHash))
        {
            throw new IOException("The recovered copy failed its SHA-256 verification check.");
        }
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

    private static bool IsFilesystemException(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException;
}
