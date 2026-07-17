using System.Globalization;
using System.Security.Cryptography;
using ListenShelf.Application.Library;
using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Infrastructure.Library;

public sealed class SqliteAudiobookLibrary : IAudiobookLibrary
{
    private readonly ListenShelfDatabase _database;

    public SqliteAudiobookLibrary(
        ListenShelfDatabase database,
        string? managedLibraryPath = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));

        var databaseDirectory = Path.GetDirectoryName(_database.DatabasePath)
            ?? throw new InvalidOperationException("The ListenShelf database needs a parent directory.");

        ManagedLibraryPath = Path.GetFullPath(
            managedLibraryPath ?? Path.Combine(databaseDirectory, "Library"));
    }

    public string ManagedLibraryPath { get; }

    public IReadOnlyList<LibraryBook> GetBooks()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT book_id, title, file_path, storage_mode, file_size_bytes, added_utc
            FROM library_books
            ORDER BY added_utc DESC, title COLLATE NOCASE;
            """;

        using var reader = command.ExecuteReader();
        var books = new List<LibraryBook>();
        while (reader.Read())
        {
            books.Add(ReadBook(reader));
        }

        return books;
    }

    public LibraryImportResult Import(string sourceFilePath, LibraryStorageMode storageMode)
    {
        if (!Enum.IsDefined(storageMode))
        {
            throw new ArgumentOutOfRangeException(nameof(storageMode));
        }

        var normalizedSourcePath = NormalizeM4bPath(sourceFilePath);
        var sourceFile = new FileInfo(normalizedSourcePath);
        if (!sourceFile.Exists)
        {
            throw new FileNotFoundException("The selected audiobook could not be found.", normalizedSourcePath);
        }

        return storageMode switch
        {
            LibraryStorageMode.Linked => ImportLinked(sourceFile),
            LibraryStorageMode.Managed => ImportManaged(sourceFile),
            _ => throw new ArgumentOutOfRangeException(nameof(storageMode)),
        };
    }

    private LibraryImportResult ImportLinked(FileInfo sourceFile)
    {
        var sourceKey = CreatePathKey(sourceFile.FullName);
        var existing = FindByFileKey(sourceKey);
        if (existing is not null)
        {
            return new LibraryImportResult(existing, WasAdded: false);
        }

        var book = CreateBook(sourceFile, sourceFile.FullName, LibraryStorageMode.Linked);
        Insert(book, sourceFile.FullName, sourceKey: null);
        return new LibraryImportResult(book, WasAdded: true);
    }

    private LibraryImportResult ImportManaged(FileInfo sourceFile)
    {
        var sourceKey = CreatePathKey(sourceFile.FullName);
        var existing = FindBySourceKey(sourceKey);
        if (existing is not null)
        {
            return new LibraryImportResult(existing, WasAdded: false);
        }

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

            var managedFile = new FileInfo(destinationPath);
            var book = new LibraryBook(
                bookId,
                Path.GetFileNameWithoutExtension(sourceFile.Name),
                managedFile.FullName,
                LibraryStorageMode.Managed,
                managedFile.Length,
                DateTimeOffset.UtcNow);

            try
            {
                Insert(book, sourceFile.FullName, sourceKey);
            }
            catch
            {
                File.Delete(destinationPath);
                throw;
            }

            return new LibraryImportResult(book, WasAdded: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            if (Directory.Exists(bookDirectory) && !Directory.EnumerateFileSystemEntries(bookDirectory).Any())
            {
                Directory.Delete(bookDirectory);
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
                $"The managed copy was incomplete: expected {sourceFile.Length} bytes but copied {copiedLength} bytes.");
        }

        using var copiedFile = File.OpenRead(temporaryPath);
        var copiedHash = SHA256.HashData(copiedFile);
        if (!CryptographicOperations.FixedTimeEquals(sourceHash, copiedHash))
        {
            throw new IOException("The managed copy failed its SHA-256 verification check.");
        }
    }

    private LibraryBook? FindByFileKey(string fileKey) =>
        Find("file_key", fileKey);

    private LibraryBook? FindBySourceKey(string sourceKey) =>
        Find("source_key", sourceKey);

    private LibraryBook? Find(string columnName, string value)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT book_id, title, file_path, storage_mode, file_size_bytes, added_utc
            FROM library_books
            WHERE {columnName} = $value;
            """;
        command.Parameters.AddWithValue("$value", value);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadBook(reader) : null;
    }

    private void Insert(LibraryBook book, string sourcePath, string? sourceKey)
    {
        var normalizedFilePath = Path.GetFullPath(book.FilePath);

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
                $storage_mode,
                $source_path,
                $source_key,
                $file_size_bytes,
                $added_utc);
            """;
        command.Parameters.AddWithValue("$book_id", book.Id.ToString("D"));
        command.Parameters.AddWithValue("$title", book.Title);
        command.Parameters.AddWithValue("$file_path", normalizedFilePath);
        command.Parameters.AddWithValue("$file_key", CreatePathKey(normalizedFilePath));
        command.Parameters.AddWithValue("$storage_mode", book.StorageMode.ToString());
        command.Parameters.AddWithValue("$source_path", sourcePath);
        command.Parameters.AddWithValue("$source_key", (object?)sourceKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$file_size_bytes", book.FileSizeBytes);
        command.Parameters.AddWithValue(
            "$added_utc",
            book.AddedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static LibraryBook CreateBook(
        FileInfo sourceFile,
        string filePath,
        LibraryStorageMode storageMode) =>
        new(
            Guid.NewGuid(),
            Path.GetFileNameWithoutExtension(sourceFile.Name),
            Path.GetFullPath(filePath),
            storageMode,
            sourceFile.Length,
            DateTimeOffset.UtcNow);

    private static LibraryBook ReadBook(Microsoft.Data.Sqlite.SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            Enum.Parse<LibraryStorageMode>(reader.GetString(3), ignoreCase: true),
            reader.GetInt64(4),
            DateTimeOffset.Parse(
                reader.GetString(5),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind));

    private static string NormalizeM4bPath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("An audiobook path is required.", nameof(filePath));
        }

        var normalizedPath = Path.GetFullPath(filePath);
        if (!string.Equals(Path.GetExtension(normalizedPath), ".m4b", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("ListenShelf currently imports M4B audiobooks only.");
        }

        return normalizedPath;
    }

    private static string CreatePathKey(string normalizedPath) =>
        OperatingSystem.IsWindows()
            ? normalizedPath.ToUpperInvariant()
            : normalizedPath;
}
