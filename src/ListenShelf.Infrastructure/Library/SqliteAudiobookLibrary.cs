using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ListenShelf.Application.Library;
using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Infrastructure.Library;

public sealed class SqliteAudiobookLibrary : IAudiobookLibrary
{
    private const string BookColumnList =
        """
        book_id,
        title,
        subtitle,
        authors_json,
        series_name,
        series_position,
        original_publication_year,
        original_publisher,
        description,
        genres_json,
        narrators_json,
        audio_publisher,
        audiobook_release_date,
        language,
        isbn_10,
        isbn_13,
        asin,
        edition_name,
        abridgement,
        edition_notes,
        file_path,
        storage_mode,
        file_size_bytes,
        added_utc,
        cover_path
        """;

    private static readonly HashSet<string> SupportedCoverExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".webp",
        };

    private readonly ListenShelfDatabase _database;
    private readonly string _coverCachePath;

    public SqliteAudiobookLibrary(
        ListenShelfDatabase database,
        string? managedLibraryPath = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));

        var databaseDirectory = Path.GetDirectoryName(_database.DatabasePath)
            ?? throw new InvalidOperationException("The ListenShelf database needs a parent directory.");

        ManagedLibraryPath = Path.GetFullPath(
            managedLibraryPath ?? Path.Combine(databaseDirectory, "Library"));
        _coverCachePath = Path.GetFullPath(Path.Combine(databaseDirectory, "Covers"));
    }

    public string ManagedLibraryPath { get; }

    public IReadOnlyList<LibraryBook> GetBooks()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT {BookColumnList}
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

    public LibraryBook SetCover(Guid bookId, string sourceImagePath)
    {
        if (bookId == Guid.Empty)
        {
            throw new ArgumentException("A valid audiobook identifier is required.", nameof(bookId));
        }

        var book = FindById(bookId)
            ?? throw new KeyNotFoundException("The selected audiobook is no longer in the library.");
        EnsureMetadataCanBeManaged(book);
        var normalizedSourcePath = NormalizeCoverPath(sourceImagePath);
        var extension = Path.GetExtension(normalizedSourcePath).ToLowerInvariant();

        Directory.CreateDirectory(_coverCachePath);

        var destinationPath = Path.Combine(_coverCachePath, $"{bookId:N}{extension}");
        var temporaryPath = Path.Combine(_coverCachePath, $"{bookId:N}.{Guid.NewGuid():N}.importing");

        if (PathsEqual(normalizedSourcePath, destinationPath))
        {
            UpdateCoverPath(bookId, destinationPath);
            return book with { CoverPath = destinationPath };
        }

        try
        {
            File.Copy(normalizedSourcePath, temporaryPath, overwrite: false);
            File.Move(temporaryPath, destinationPath, overwrite: true);
            UpdateCoverPath(bookId, destinationPath);

            if (!string.IsNullOrWhiteSpace(book.CoverPath)
                && !PathsEqual(book.CoverPath, destinationPath))
            {
                TryDeleteOldCover(book.CoverPath);
            }

            return book with { CoverPath = destinationPath };
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            throw;
        }
    }

    public LibraryBook UpdateMetadata(Guid bookId, AudiobookMetadata metadata)
    {
        if (bookId == Guid.Empty)
        {
            throw new ArgumentException("A valid audiobook identifier is required.", nameof(bookId));
        }

        ArgumentNullException.ThrowIfNull(metadata);
        var book = FindById(bookId)
            ?? throw new KeyNotFoundException("The selected audiobook is no longer in the library.");
        EnsureMetadataCanBeManaged(book);
        var normalizedMetadata = NormalizeMetadata(metadata);

        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE library_books
            SET title = $title,
                subtitle = $subtitle,
                authors_json = $authors_json,
                series_name = $series_name,
                series_position = $series_position,
                original_publication_year = $original_publication_year,
                original_publisher = $original_publisher,
                description = $description,
                genres_json = $genres_json,
                narrators_json = $narrators_json,
                audio_publisher = $audio_publisher,
                audiobook_release_date = $audiobook_release_date,
                language = $language,
                isbn_10 = $isbn_10,
                isbn_13 = $isbn_13,
                asin = $asin,
                edition_name = $edition_name,
                abridgement = $abridgement,
                edition_notes = $edition_notes
            WHERE book_id = $book_id;
            """;
        AddMetadataParameters(command, normalizedMetadata);
        command.Parameters.AddWithValue("$book_id", bookId.ToString("D"));

        if (command.ExecuteNonQuery() != 1)
        {
            throw new KeyNotFoundException("The selected audiobook is no longer in the library.");
        }

        return FindById(bookId)
            ?? throw new KeyNotFoundException("The selected audiobook is no longer in the library.");
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
                AudiobookMetadata.FromFileName(Path.GetFileNameWithoutExtension(sourceFile.Name)),
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

    private LibraryBook? FindById(Guid bookId) =>
        Find("book_id", bookId.ToString("D"));

    private LibraryBook? Find(string columnName, string value)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT {BookColumnList}
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
                added_utc,
                cover_path,
                subtitle,
                authors_json,
                series_name,
                series_position,
                original_publication_year,
                original_publisher,
                description,
                genres_json,
                narrators_json,
                audio_publisher,
                audiobook_release_date,
                language,
                isbn_10,
                isbn_13,
                asin,
                edition_name,
                abridgement,
                edition_notes)
            VALUES (
                $book_id,
                $title,
                $file_path,
                $file_key,
                $storage_mode,
                $source_path,
                $source_key,
                $file_size_bytes,
                $added_utc,
                $cover_path,
                $subtitle,
                $authors_json,
                $series_name,
                $series_position,
                $original_publication_year,
                $original_publisher,
                $description,
                $genres_json,
                $narrators_json,
                $audio_publisher,
                $audiobook_release_date,
                $language,
                $isbn_10,
                $isbn_13,
                $asin,
                $edition_name,
                $abridgement,
                $edition_notes);
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
        command.Parameters.AddWithValue("$cover_path", (object?)book.CoverPath ?? DBNull.Value);
        AddMetadataParameters(command, book.Metadata, includeTitle: false);
        command.ExecuteNonQuery();
    }

    private void UpdateCoverPath(Guid bookId, string coverPath)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE library_books
            SET cover_path = $cover_path
            WHERE book_id = $book_id;
            """;
        command.Parameters.AddWithValue("$cover_path", Path.GetFullPath(coverPath));
        command.Parameters.AddWithValue("$book_id", bookId.ToString("D"));

        if (command.ExecuteNonQuery() != 1)
        {
            throw new KeyNotFoundException("The selected audiobook is no longer in the library.");
        }
    }

    private static LibraryBook CreateBook(
        FileInfo sourceFile,
        string filePath,
        LibraryStorageMode storageMode) =>
        new(
            Guid.NewGuid(),
            AudiobookMetadata.FromFileName(Path.GetFileNameWithoutExtension(sourceFile.Name)),
            Path.GetFullPath(filePath),
            storageMode,
            sourceFile.Length,
            DateTimeOffset.UtcNow);

    private static LibraryBook ReadBook(Microsoft.Data.Sqlite.SqliteDataReader reader)
    {
        var metadata = new AudiobookMetadata
        {
            Title = reader.GetString(1),
            Subtitle = ReadNullableString(reader, 2),
            Authors = ReadStringList(reader, 3),
            SeriesName = ReadNullableString(reader, 4),
            SeriesPosition = ReadNullableString(reader, 5),
            OriginalPublicationYear = reader.IsDBNull(6) ? null : reader.GetInt32(6),
            OriginalPublisher = ReadNullableString(reader, 7),
            Description = ReadNullableString(reader, 8),
            Genres = ReadStringList(reader, 9),
            Narrators = ReadStringList(reader, 10),
            AudioPublisher = ReadNullableString(reader, 11),
            AudiobookReleaseDate = ReadDateOnly(reader, 12),
            Language = ReadNullableString(reader, 13),
            Isbn10 = ReadNullableString(reader, 14),
            Isbn13 = ReadNullableString(reader, 15),
            Asin = ReadNullableString(reader, 16),
            EditionName = ReadNullableString(reader, 17),
            Abridgement = ReadAbridgement(reader, 18),
            EditionNotes = ReadNullableString(reader, 19),
        };

        return new LibraryBook(
            Guid.Parse(reader.GetString(0)),
            metadata,
            reader.GetString(20),
            Enum.Parse<LibraryStorageMode>(reader.GetString(21), ignoreCase: true),
            reader.GetInt64(22),
            DateTimeOffset.Parse(
                reader.GetString(23),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind),
            ReadNullableString(reader, 24));
    }

    private static void AddMetadataParameters(
        Microsoft.Data.Sqlite.SqliteCommand command,
        AudiobookMetadata metadata,
        bool includeTitle = true)
    {
        if (includeTitle)
        {
            command.Parameters.AddWithValue("$title", metadata.Title);
        }

        AddNullableTextParameter(command, "$subtitle", metadata.Subtitle);
        command.Parameters.AddWithValue("$authors_json", JsonSerializer.Serialize(metadata.Authors));
        AddNullableTextParameter(command, "$series_name", metadata.SeriesName);
        AddNullableTextParameter(command, "$series_position", metadata.SeriesPosition);
        command.Parameters.AddWithValue(
            "$original_publication_year",
            (object?)metadata.OriginalPublicationYear ?? DBNull.Value);
        AddNullableTextParameter(command, "$original_publisher", metadata.OriginalPublisher);
        AddNullableTextParameter(command, "$description", metadata.Description);
        command.Parameters.AddWithValue("$genres_json", JsonSerializer.Serialize(metadata.Genres));
        command.Parameters.AddWithValue("$narrators_json", JsonSerializer.Serialize(metadata.Narrators));
        AddNullableTextParameter(command, "$audio_publisher", metadata.AudioPublisher);
        AddNullableTextParameter(
            command,
            "$audiobook_release_date",
            metadata.AudiobookReleaseDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        AddNullableTextParameter(command, "$language", metadata.Language);
        AddNullableTextParameter(command, "$isbn_10", metadata.Isbn10);
        AddNullableTextParameter(command, "$isbn_13", metadata.Isbn13);
        AddNullableTextParameter(command, "$asin", metadata.Asin);
        AddNullableTextParameter(command, "$edition_name", metadata.EditionName);
        command.Parameters.AddWithValue("$abridgement", metadata.Abridgement.ToString());
        AddNullableTextParameter(command, "$edition_notes", metadata.EditionNotes);
    }

    private static void AddNullableTextParameter(
        Microsoft.Data.Sqlite.SqliteCommand command,
        string parameterName,
        string? value) =>
        command.Parameters.AddWithValue(parameterName, (object?)value ?? DBNull.Value);

    private static AudiobookMetadata NormalizeMetadata(AudiobookMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata.Title))
        {
            throw new ArgumentException("An audiobook title is required.", nameof(metadata));
        }

        var title = metadata.Title.Trim();

        if (metadata.OriginalPublicationYear is < 1 or > 9999)
        {
            throw new ArgumentOutOfRangeException(
                nameof(metadata),
                "The original publication year must be between 1 and 9999.");
        }

        if (!Enum.IsDefined(metadata.Abridgement))
        {
            throw new ArgumentOutOfRangeException(nameof(metadata), "The abridgement value is invalid.");
        }

        return metadata with
        {
            Title = title,
            Subtitle = NormalizeOptionalText(metadata.Subtitle),
            Authors = NormalizeTextList(metadata.Authors),
            SeriesName = NormalizeOptionalText(metadata.SeriesName),
            SeriesPosition = NormalizeOptionalText(metadata.SeriesPosition),
            OriginalPublisher = NormalizeOptionalText(metadata.OriginalPublisher),
            Description = NormalizeOptionalText(metadata.Description),
            Genres = NormalizeTextList(metadata.Genres),
            Narrators = NormalizeTextList(metadata.Narrators),
            AudioPublisher = NormalizeOptionalText(metadata.AudioPublisher),
            Language = NormalizeOptionalText(metadata.Language),
            Isbn10 = NormalizeOptionalText(metadata.Isbn10),
            Isbn13 = NormalizeOptionalText(metadata.Isbn13),
            Asin = NormalizeOptionalText(metadata.Asin),
            EditionName = NormalizeOptionalText(metadata.EditionName),
            EditionNotes = NormalizeOptionalText(metadata.EditionNotes),
        };
    }

    private static void EnsureMetadataCanBeManaged(LibraryBook book)
    {
        if (book.StorageMode != LibraryStorageMode.Managed)
        {
            throw new InvalidOperationException(
                "Player Only Mode entries do not support metadata or cover editing.");
        }
    }

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> NormalizeTextList(IEnumerable<string> values) =>
        values
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string? ReadNullableString(
        Microsoft.Data.Sqlite.SqliteDataReader reader,
        int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static IReadOnlyList<string> ReadStringList(
        Microsoft.Data.Sqlite.SqliteDataReader reader,
        int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(reader.GetString(ordinal)) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static DateOnly? ReadDateOnly(
        Microsoft.Data.Sqlite.SqliteDataReader reader,
        int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : DateOnly.ParseExact(
                reader.GetString(ordinal),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture);

    private static AudiobookAbridgement ReadAbridgement(
        Microsoft.Data.Sqlite.SqliteDataReader reader,
        int ordinal) =>
        !reader.IsDBNull(ordinal)
        && Enum.TryParse<AudiobookAbridgement>(reader.GetString(ordinal), ignoreCase: true, out var value)
            ? value
            : AudiobookAbridgement.Unknown;

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

    private static string NormalizeCoverPath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("A cover image path is required.", nameof(filePath));
        }

        var normalizedPath = Path.GetFullPath(filePath);
        if (!SupportedCoverExtensions.Contains(Path.GetExtension(normalizedPath)))
        {
            throw new NotSupportedException("ListenShelf currently supports PNG, JPEG, and WebP cover images.");
        }

        if (!File.Exists(normalizedPath))
        {
            throw new FileNotFoundException("The selected cover image could not be found.", normalizedPath);
        }

        return normalizedPath;
    }

    private void TryDeleteOldCover(string oldCoverPath)
    {
        try
        {
            var normalizedOldPath = Path.GetFullPath(oldCoverPath);
            var normalizedCachePath = Path.TrimEndingDirectorySeparator(_coverCachePath)
                + Path.DirectorySeparatorChar;

            if (normalizedOldPath.StartsWith(
                    normalizedCachePath,
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal)
                && File.Exists(normalizedOldPath))
            {
                File.Delete(normalizedOldPath);
            }
        }
        catch
        {
            // An orphaned cache file is harmless and can be cleaned up later.
        }
    }

    private static bool PathsEqual(string firstPath, string secondPath) =>
        string.Equals(
            Path.GetFullPath(firstPath),
            Path.GetFullPath(secondPath),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    private static string CreatePathKey(string normalizedPath) =>
        OperatingSystem.IsWindows()
            ? normalizedPath.ToUpperInvariant()
            : normalizedPath;
}
