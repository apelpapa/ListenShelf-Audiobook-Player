using System.Security.Cryptography;
using ListenShelf.Application.Library;

namespace ListenShelf.Infrastructure.Library;

public sealed partial class SqliteAudiobookLibrary
{
    public ManagedFileRepairResult Repair(Guid bookId, string sourceFilePath,
        IProgress<LibraryImportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (bookId == Guid.Empty) throw new ArgumentException("Select a valid audiobook to repair.", nameof(bookId));
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new LibraryImportProgress(LibraryImportStage.Checking, Detail: "Checking repair paths and saved fingerprint"));
        using var operationLock = AcquireImportLock(progress, cancellationToken);
        var reference = ReadRepairReference(bookId);
        var targetPath = ValidateRepairTarget(reference);
        var sourcePath = Path.GetFullPath(sourceFilePath);
        EnsureRepairPathHasNoLinks(sourcePath);
        if (PathsEqual(targetPath, sourcePath))
            throw new IOException("Choose the original or a separate known-good copy, not the managed file being repaired.");

        // The selected file is never written to, moved, or removed. Holding it
        // read-only also prevents writes/deletion through hard-link aliases on Windows.
        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (source.Length != reference.Length)
            throw new IOException("The selected file has a different size from the original import. Nothing was replaced.");

        var directory = Path.GetDirectoryName(targetPath)!;
        var createdDirectory = !Directory.Exists(directory);
        Directory.CreateDirectory(directory);
        ValidateRepairTarget(reference);
        var attempt = Guid.NewGuid().ToString("N");
        var temporaryPath = Path.Combine(directory, attempt + ".repairing");
        var retainedPath = Path.Combine(directory, attempt + ".repair-backup");
        var finalizationStarted = false;
        try
        {
            CopyRepairSource(source, temporaryPath, reference, progress, cancellationToken);
            // Keep Windows writers out through verification and final checks.
            // ReplaceFile itself requires a write-capable open, so release this
            // guard immediately before that call, with no intervening callbacks.
            using var verifiedCopyGuard = new FileStream(temporaryPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var copiedHash = ReadFingerprint(temporaryPath, reference.Length, LibraryImportStage.Verifying, progress, cancellationToken);
            if (!string.Equals(copiedHash, reference.Hash, StringComparison.Ordinal))
                throw new IOException("The copied file failed verification against the saved fingerprint. Nothing was replaced.");

            cancellationToken.ThrowIfCancellationRequested();
            // Recheck authoritative identity immediately before changing any managed
            // bytes. Repair never changes the catalog, progress, or saved baseline.
            if (ReadRepairReference(bookId) != reference)
                throw new IOException("The catalog entry changed during repair. Nothing was replaced; refresh and retry.");
            ValidateRepairTarget(reference);
            EnsureRepairPathHasNoLinks(temporaryPath);
            if (File.Exists(retainedPath) || Directory.Exists(retainedPath))
                throw new IOException("The repair backup path already exists. Nothing was replaced.");

            progress?.Report(new LibraryImportProgress(LibraryImportStage.Finalizing,
                Detail: "Installing verified copy; finishing safely before cancellation"));
            // Cancellation stops before this boundary; never interrupt a replacement.
            verifiedCopyGuard.Dispose();
            finalizationStarted = true;
            if (File.Exists(targetPath))
            {
                // All three paths share one directory/volume. Never fall back to
                // deleting the target first. On a filesystem failure retain all
                // remaining artifacts, including the verified temporary copy.
                File.Replace(temporaryPath, targetPath, retainedPath);
                return new ManagedFileRepairResult(bookId, reference.Title, targetPath, retainedPath);
            }

            File.Move(temporaryPath, targetPath, overwrite: false);
            return new ManagedFileRepairResult(bookId, reference.Title, targetPath, null);
        }
        catch (Exception exception) when (finalizationStarted)
        {
            throw new IOException("Repair could not confirm the final replacement. Remaining repair files were kept in Storage Care. "
                + "Verify this book before retrying; no catalog or listening data was changed. " + exception.Message, exception);
        }
        finally
        {
            if (!finalizationStarted)
            {
                try
                {
                    EnsureRepairPathHasNoLinks(temporaryPath);
                    File.Delete(temporaryPath);
                    if (createdDirectory && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    throw new IOException("Repair stopped, but its unfinished copy could not be removed. Review Storage Care for confirmed cleanup. "
                        + "The managed audiobook and source were not replaced.", exception);
                }
            }
        }
    }

    private RepairReference ReadRepairReference(Guid bookId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT title, file_path, file_size_bytes, content_sha256 FROM library_books AS books
            WHERE book_id = $id AND storage_mode = 'Managed'
              AND NOT EXISTS (SELECT 1 FROM pending_library_removals WHERE book_id = books.book_id);
            """;
        command.Parameters.AddWithValue("$id", bookId.ToString("D"));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new KeyNotFoundException("The selected audiobook is no longer available for repair.");
        if (reader.IsDBNull(3))
            throw new InvalidOperationException("This book has no saved fingerprint. Safe repair cannot verify its original contents; restore a known-good library backup instead. No baseline was created.");
        var hash = reader.GetString(3);
        if (hash.Length != 64 || hash.Any(character => !char.IsAsciiHexDigit(character)))
            throw new InvalidOperationException("The saved fingerprint is invalid. Repair will not create a replacement baseline.");
        return new RepairReference(bookId, reader.GetString(0), reader.GetString(1), reader.GetInt64(2), hash.ToUpperInvariant());
    }

    private string ValidateRepairTarget(RepairReference book)
    {
        var path = Path.GetFullPath(book.Path);
        if (!PathsEqual(Path.GetDirectoryName(path)!, Path.Combine(ManagedLibraryPath, book.Id.ToString("N")))
            || !AudiobookFileFormats.IsSupported(path))
            throw new IOException("Repair refused an unsafe catalog path outside the expected managed audiobook location.");
        EnsureRepairPathHasNoLinks(path);
        if (Directory.Exists(path)) throw new IOException("The audiobook path is occupied by a directory. Nothing was replaced.");
        return path;
    }

    private static void EnsureRepairPathHasNoLinks(string path)
    {
        var ancestors = new Stack<string>();
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current)) ancestors.Push(current);
        foreach (var current in ancestors)
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Repair will not follow filesystem links or junctions.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static void CopyRepairSource(FileStream source, string temporaryPath, RepairReference reference,
        IProgress<LibraryImportProgress>? progress, CancellationToken token)
    {
        using var target = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        var processed = 0L;
        progress?.Report(new LibraryImportProgress(LibraryImportStage.Copying, 0, reference.Length));
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var count = source.Read(buffer, 0, buffer.Length);
            if (count == 0) break;
            target.Write(buffer, 0, count);
            hash.AppendData(buffer, 0, count);
            processed += count;
            progress?.Report(new LibraryImportProgress(LibraryImportStage.Copying, processed, reference.Length));
        }

        token.ThrowIfCancellationRequested();
        if (processed != reference.Length || source.Length != reference.Length
            || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(reference.Hash)))
            throw new IOException("The selected file does not match this book's saved SHA-256 fingerprint. Choose the exact original import; nothing was replaced.");
        target.Flush(flushToDisk: true);
    }

    private sealed record RepairReference(Guid Id, string Title, string Path, long Length, string Hash);
}
