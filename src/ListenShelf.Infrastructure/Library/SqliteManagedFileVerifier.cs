using System.Security.Cryptography;
using ListenShelf.Application.Library;
using ListenShelf.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace ListenShelf.Infrastructure.Library;

public sealed class SqliteManagedFileVerifier(ListenShelfDatabase database, string managedLibraryPath) : IManagedFileVerifier
{
    private readonly string _managedRoot = Path.GetFullPath(managedLibraryPath);

    public ManagedFileVerificationReport Verify(Guid? bookId = null,
        IProgress<ManagedFileVerificationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (bookId == Guid.Empty)
        {
            throw new ArgumentException("Select a valid audiobook to verify.", nameof(bookId));
        }

        var books = ReadBooks(bookId);
        if (bookId is not null && books.Count == 0)
        {
            throw new KeyNotFoundException("The selected audiobook is no longer in the managed library. Refresh the library and try again.");
        }

        var results = new List<ManagedFileVerificationResult>();
        for (var index = 0; index < books.Count; index++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var book = books[index];
            var number = index + 1;
            ManagedFileVerificationResult result;
            try
            {
                result = VerifyBook(book, (done, total) => progress?.Report(
                    new ManagedFileVerificationProgress(number, books.Count, book.Title, done, total)), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                result = Result(book, ManagedFileVerificationStatus.Canceled, "Stopped before this book was fully verified. No files or fingerprints were changed.");
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                result = Result(book, ManagedFileVerificationStatus.Missing, "The managed audiobook file or its folder is missing. Check Storage Care or restore a library backup.");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                result = Result(book, ManagedFileVerificationStatus.Unreadable, $"Could not safely read this managed file: {exception.Message}");
            }

            results.Add(result);
            progress?.Report(new ManagedFileVerificationProgress(number, books.Count, book.Title, 0, 0, result));
        }

        foreach (var book in books.Skip(results.Count))
        {
            results.Add(Result(book, ManagedFileVerificationStatus.NotChecked, "Not checked because the scan was canceled."));
        }

        return new ManagedFileVerificationReport(DateTimeOffset.UtcNow, results.ToArray());
    }

    private ManagedFileVerificationResult VerifyBook(BookReference book, Action<long, long> report, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        report(0, book.Length);
        var path = Path.GetFullPath(book.Path);
        var expectedParent = Path.Combine(_managedRoot, book.Id.ToString("N"));
        if (!string.Equals(Path.GetDirectoryName(path), expectedParent,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new IOException("The catalog path is outside its expected managed book folder.");
        }

        // Inspect ancestors first so a dangling link is not mistaken for a missing
        // book, and never follow a link to files outside managed storage.
        var ancestors = new Stack<string>();
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            ancestors.Push(current);
        }

        foreach (var current in ancestors)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("The managed path contains a filesystem link or junction.");
            }
        }

        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        token.ThrowIfCancellationRequested();
        if (book.Hash is null)
        {
            return Result(book, ManagedFileVerificationStatus.NoBaseline,
                "No saved SHA-256 fingerprint exists for this older or recovered book. Its original contents cannot be verified; no baseline was created.");
        }

        if (book.Hash.Length != 64 || book.Hash.Any(character => !char.IsAsciiHexDigit(character)))
        {
            throw new IOException("The saved fingerprint is invalid. No new baseline was created.");
        }

        if (file.Length != book.Length)
        {
            return Result(book, ManagedFileVerificationStatus.Changed,
                "The file size differs from the imported copy. The file and its saved fingerprint were left untouched.");
        }

        var modifiedAt = File.GetLastWriteTimeUtc(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        var processed = 0L;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var count = file.Read(buffer, 0, buffer.Length);
            if (count == 0)
            {
                break;
            }

            hash.AppendData(buffer, 0, count);
            processed += count;
            report(processed, book.Length);
        }

        token.ThrowIfCancellationRequested();
        if (processed != book.Length || file.Length != book.Length || File.GetLastWriteTimeUtc(path) != modifiedAt)
        {
            throw new IOException("The file changed during verification. Run the check again when it is no longer being edited.");
        }

        var matches = CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(book.Hash));
        return matches
            ? Result(book, ManagedFileVerificationStatus.Unchanged, "The file matches its saved SHA-256 fingerprint. This checks file integrity, not audio playability.")
            : Result(book, ManagedFileVerificationStatus.Changed, "The file contents differ from the saved SHA-256 fingerprint. This may indicate corruption or an external edit. Nothing was replaced or accepted as a new baseline.");
    }

    private IReadOnlyList<BookReference> ReadBooks(Guid? bookId)
    {
        // Opening read-only also prevents an accidental write in this scan service.
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = database.DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            DefaultTimeout = 5,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT book_id, title, file_path, file_size_bytes, content_sha256
            FROM library_books AS books
            WHERE storage_mode = 'Managed' AND ($id IS NULL OR book_id = $id)
              AND NOT EXISTS (SELECT 1 FROM pending_library_removals AS removals WHERE removals.book_id = books.book_id)
            ORDER BY title COLLATE NOCASE, book_id;
            """;
        command.Parameters.AddWithValue("$id", (object?)bookId?.ToString("D") ?? DBNull.Value);
        using var reader = command.ExecuteReader();
        var books = new List<BookReference>();
        while (reader.Read())
        {
            books.Add(new BookReference(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2),
                reader.GetInt64(3), reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return books;
    }

    private static ManagedFileVerificationResult Result(BookReference book, ManagedFileVerificationStatus status, string detail) =>
        new(book.Id, book.Title, book.Path, status, detail, DateTimeOffset.UtcNow);

    private sealed record BookReference(Guid Id, string Title, string Path, long Length, string? Hash);
}
