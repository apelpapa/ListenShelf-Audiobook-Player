using System.Security.Cryptography;
using ListenShelf.Application.Library;

namespace ListenShelf.Infrastructure.Library;

public sealed partial class SqliteAudiobookLibrary
{
    private IReadOnlyList<FingerprintCandidate> GetFingerprintCandidates(long length)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT {BookColumnList}, content_sha256
            FROM library_books
            WHERE storage_mode = 'Managed' AND file_size_bytes = $length
              AND NOT EXISTS (SELECT 1 FROM pending_library_removals AS removals
                              WHERE removals.book_id = library_books.book_id)
            ORDER BY content_sha256 IS NULL, added_utc, book_id;
            """;
        command.Parameters.AddWithValue("$length", length);
        using var reader = command.ExecuteReader();
        var candidates = new List<FingerprintCandidate>();
        while (reader.Read())
        {
            candidates.Add(new FingerprintCandidate(ReadBook(reader), ReadNullableString(reader, 24)));
        }

        return candidates;
    }

    private LibraryBook? FindContentDuplicate(
        IReadOnlyList<FingerprintCandidate> candidates, string sourceHash,
        IProgress<LibraryImportProgress>? progress, CancellationToken cancellationToken)
    {
        IOException? comparisonFailure = null;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate.Hash is not null && candidate.Hash != sourceHash)
            {
                continue;
            }

            string actualHash;
            try
            {
                ValidateFingerprintPath(candidate.Book);
                // Even a stored match is reread before skipping an import: a
                // missing or damaged managed copy must not be called a duplicate.
                actualHash = ReadFingerprint(candidate.Book.FilePath, candidate.Book.FileSizeBytes,
                    LibraryImportStage.Comparing, progress, cancellationToken, candidate.Book.Title);
                if (candidate.Hash is not null && candidate.Hash != actualHash)
                {
                    throw new IOException("Its managed copy no longer matches its saved fingerprint.");
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                comparisonFailure = new IOException(
                    $"Could not check the existing book \"{candidate.Book.Title}\": {exception.Message} "
                    + "No new copy was made. Check Storage Care or restore a library backup, then retry.", exception);
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (candidate.Hash is null)
            {
                SaveLegacyFingerprint(candidate.Book.Id, actualHash);
            }

            if (actualHash == sourceHash)
            {
                return FindById(candidate.Book.Id)
                    ?? throw new IOException("The matching book was removed while checking duplicates. Please retry the import.");
            }
        }

        if (comparisonFailure is not null)
        {
            throw comparisonFailure;
        }

        return null;
    }

    private void SaveLegacyFingerprint(Guid bookId, string hash)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE library_books SET content_sha256 = $hash
            WHERE book_id = $id AND content_sha256 IS NULL;
            """;
        command.Parameters.AddWithValue("$id", bookId.ToString("D"));
        command.Parameters.AddWithValue("$hash", hash);
        command.ExecuteNonQuery();
    }

    private void ValidateFingerprintPath(LibraryBook book)
    {
        var path = Path.GetFullPath(book.FilePath);
        var expectedDirectory = Path.Combine(ManagedLibraryPath, book.Id.ToString("N"));
        if (!PathsEqual(Path.GetDirectoryName(path)!, expectedDirectory))
        {
            throw new IOException("The catalog path is outside its managed book folder.");
        }

        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("The managed path contains a filesystem link or junction.");
            }
        }
    }

    private static string ReadFingerprint(
        string path, long expectedLength, LibraryImportStage stage,
        IProgress<LibraryImportProgress>? progress, CancellationToken cancellationToken, string? detail = null)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length != expectedLength)
        {
            throw new IOException("The audiobook's size no longer matches its recorded size.");
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        var processed = 0L;
        progress?.Report(new LibraryImportProgress(stage, 0, expectedLength, detail));
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = file.Read(buffer, 0, buffer.Length);
            if (count == 0)
            {
                break;
            }

            hash.AppendData(buffer, 0, count);
            processed += count;
            progress?.Report(new LibraryImportProgress(stage, processed, expectedLength, detail));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (processed != expectedLength || file.Length != expectedLength)
        {
            throw new IOException("The audiobook changed while its fingerprint was being checked.");
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private IDisposable AcquireImportLock(IProgress<LibraryImportProgress>? progress, CancellationToken cancellationToken) =>
        ManagedLibraryOperationLock.Acquire(_database.DatabasePath, cancellationToken,
            () => progress?.Report(new LibraryImportProgress(LibraryImportStage.Checking,
                Detail: "Waiting for another library operation to finish")));

    private sealed record FingerprintCandidate(LibraryBook Book, string? Hash);

}
