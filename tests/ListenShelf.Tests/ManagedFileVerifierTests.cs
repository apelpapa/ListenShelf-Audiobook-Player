using ListenShelf.Application.Library;
using ListenShelf.Infrastructure.Library;
using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Tests;

public sealed class ManagedFileVerifierTests
{
    [Fact]
    public void FullScan_DistinguishesUnchangedChangedMissingAndNoBaselineWithoutWriting()
    {
        using var workspace = new TestWorkspace();
        var (database, library, verifier) = Create(workspace);
        var healthy = library.Import(workspace.CreateSourceFile("A healthy 日本語.m4b", [1, 2, 3])).Book;
        var changed = library.Import(workspace.CreateSourceFile("B changed.mp3", [4, 5, 6])).Book;
        var missing = library.Import(workspace.CreateSourceFile("C missing.m4a", [7, 8, 9])).Book;
        var legacy = library.Import(workspace.CreateSourceFile("D older.m4b", [10, 11, 12])).Book;
        File.WriteAllBytes(changed.FilePath, [6, 5, 4]);
        File.Delete(missing.FilePath);
        Execute(database, $"UPDATE library_books SET content_sha256 = NULL WHERE book_id = '{legacy.Id:D}';");
        var savedAt = DateTimeOffset.UtcNow;
        new ListenShelf.Infrastructure.Progress.SqlitePlaybackProgressStore(database).Save(
            new ListenShelf.Application.Progress.PlaybackProgress(healthy.FilePath, TimeSpan.FromMinutes(2), TimeSpan.FromHours(3), savedAt));
        new ListenShelf.Infrastructure.Bookmarks.SqlitePlaybackBookmarkStore(database).Save(
            new ListenShelf.Application.Bookmarks.PlaybackBookmark(Guid.NewGuid(), healthy.FilePath, TimeSpan.FromMinutes(1),
                "Keep this", "Note", 0, "Chapter 1", savedAt, savedAt));
        Execute(database, "INSERT INTO app_settings (setting_key, setting_value) VALUES ('test.preserve', 'unchanged');");
        var before = Snapshot(database);
        var originalTime = File.GetLastWriteTimeUtc(healthy.FilePath);

        var report = verifier.Verify();

        Assert.Equal([ManagedFileVerificationStatus.Unchanged, ManagedFileVerificationStatus.Changed,
            ManagedFileVerificationStatus.Missing, ManagedFileVerificationStatus.NoBaseline], report.Results.Select(result => result.Status));
        Assert.False(report.WasCanceled);
        Assert.Contains("1 without a baseline", report.Summary);
        Assert.Equal(before, Snapshot(database));
        Assert.Equal(originalTime, File.GetLastWriteTimeUtc(healthy.FilePath));
        Assert.Equal(new byte[] { 6, 5, 4 }, File.ReadAllBytes(changed.FilePath));
        Assert.Equal(new byte[] { 10, 11, 12 }, File.ReadAllBytes(legacy.FilePath));
        Assert.False(File.Exists(missing.FilePath));
        Assert.Equal(4, library.GetBooks().Count);
    }

    [Fact]
    public void ReportsActualByteProgressAndChecksOnlyTheSelectedBook()
    {
        using var workspace = new TestWorkspace();
        var (_, library, verifier) = Create(workspace);
        var bytes = new byte[3 * 1024 * 1024 + 7];
        var selected = library.Import(workspace.CreateSourceFile("Selected.m4b", bytes)).Book;
        var other = library.Import(workspace.CreateSourceFile("Other.mp3", [9])).Book;
        using var held = new FileStream(other.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var updates = new List<ManagedFileVerificationProgress>();

        var report = verifier.Verify(selected.Id, new InlineProgress<ManagedFileVerificationProgress>(updates.Add));

        Assert.Equal(selected.Id, Assert.Single(report.Results).BookId);
        Assert.Equal(ManagedFileVerificationStatus.Unchanged, report.Results[0].Status);
        Assert.All(updates, update => Assert.Equal(1, update.TotalBooks));
        Assert.Contains(updates, update => update.ProcessedBytes > 0 && update.ProcessedBytes < bytes.Length);
        Assert.Contains(updates, update => update.ProcessedBytes == bytes.Length);
        Assert.Equal(100, updates[^1].OverallPercentage);
        Assert.Equal(updates.OrderBy(update => update.OverallPercentage), updates);
    }

    [Fact]
    public void CancelingMidFile_RetainsCompletedResultsAndMarksRemainingBooksUnchecked()
    {
        using var workspace = new TestWorkspace();
        using var cancellation = new CancellationTokenSource();
        var (database, library, verifier) = Create(workspace);
        library.Import(workspace.CreateSourceFile("A complete.m4b", [1, 2]));
        var current = library.Import(workspace.CreateSourceFile("B canceled.m4b", new byte[3 * 1024 * 1024])).Book;
        library.Import(workspace.CreateSourceFile("C unstarted.mp3", [3, 4]));
        var before = Snapshot(database);

        var report = verifier.Verify(progress: new InlineProgress<ManagedFileVerificationProgress>(update =>
        {
            if (update.BookNumber == 2 && update.ProcessedBytes > 0) cancellation.Cancel();
        }), cancellationToken: cancellation.Token);

        Assert.True(report.WasCanceled);
        Assert.Equal([ManagedFileVerificationStatus.Unchanged, ManagedFileVerificationStatus.Canceled,
            ManagedFileVerificationStatus.NotChecked], report.Results.Select(result => result.Status));
        Assert.Equal(before, Snapshot(database));
        Assert.Equal(3 * 1024 * 1024, new FileInfo(current.FilePath).Length);
        // The canceled read releases its handle.
        using var exclusive = new FileStream(current.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public void CanceledBeforeStart_DoesNotClaimAnyBookWasVerified()
    {
        using var workspace = new TestWorkspace();
        var (_, library, verifier) = Create(workspace);
        library.Import(workspace.CreateSourceFile("Book.mp3", [1]));
        var report = verifier.Verify(cancellationToken: new CancellationToken(true));
        Assert.Equal(ManagedFileVerificationStatus.NotChecked, Assert.Single(report.Results).Status);
        Assert.True(report.WasCanceled);
    }

    [Fact]
    public void ChangedSize_IsReportedWithoutAcceptingANewFingerprint()
    {
        using var workspace = new TestWorkspace();
        var (database, library, verifier) = Create(workspace);
        var book = library.Import(workspace.CreateSourceFile("Size.mp3", [1, 2, 3])).Book;
        File.WriteAllBytes(book.FilePath, [1]);
        var before = Snapshot(database);
        Assert.Equal(ManagedFileVerificationStatus.Changed, Assert.Single(verifier.Verify().Results).Status);
        Assert.Equal(before, Snapshot(database));
    }

    [Fact]
    public void OutsideManagedPath_IsNotFollowedOrFingerprinted()
    {
        using var workspace = new TestWorkspace();
        var (database, library, verifier) = Create(workspace);
        var source = workspace.CreateSourceFile("Original.m4b", [1, 2, 3]);
        library.Import(source);
        Execute(database, $"UPDATE library_books SET file_path = '{source.Replace("'", "''")}', content_sha256 = NULL;");
        var before = Snapshot(database);
        var result = Assert.Single(verifier.Verify().Results);
        Assert.Equal(ManagedFileVerificationStatus.Unreadable, result.Status);
        Assert.Contains("outside", result.Detail);
        Assert.Equal(before, Snapshot(database));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(source));
    }

    [Fact]
    public void LockedFile_IsUnreadableAndDoesNotStopOtherChecks()
    {
        using var workspace = new TestWorkspace();
        var (_, library, verifier) = Create(workspace);
        var locked = library.Import(workspace.CreateSourceFile("A locked.m4b", [1, 2])).Book;
        library.Import(workspace.CreateSourceFile("B available.mp3", [3, 4]));
        using var held = new FileStream(locked.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var report = verifier.Verify();
        Assert.Equal([ManagedFileVerificationStatus.Unreadable, ManagedFileVerificationStatus.Unchanged],
            report.Results.Select(result => result.Status));
    }

    [Fact]
    public void MissingLegacyBook_IsMissingRatherThanUnverified()
    {
        using var workspace = new TestWorkspace();
        var (database, library, verifier) = Create(workspace);
        var book = library.Import(workspace.CreateSourceFile("Legacy.m4b", [1, 2])).Book;
        Execute(database, "UPDATE library_books SET content_sha256 = NULL;");
        File.Delete(book.FilePath);
        Assert.Equal(ManagedFileVerificationStatus.Missing, Assert.Single(verifier.Verify().Results).Status);
    }

    [Fact]
    public void EmptyLibraryAndUnknownSelection_AreNotReportedAsVerified()
    {
        using var workspace = new TestWorkspace();
        var (_, _, verifier) = Create(workspace);
        Assert.Empty(verifier.Verify().Results);
        Assert.Contains("No managed audiobooks", verifier.Verify().Summary);
        Assert.Throws<KeyNotFoundException>(() => verifier.Verify(Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => verifier.Verify(Guid.Empty));
    }

    [Fact]
    public void PendingRemoval_IsExcludedWithoutCompletingOrDeletingIt()
    {
        using var workspace = new TestWorkspace();
        var (database, library, verifier) = Create(workspace);
        var book = library.Import(workspace.CreateSourceFile("Pending.mp3", [1, 2])).Book;
        Execute(database, $"""
            INSERT INTO pending_library_removals (book_id, title, file_path, cover_path, requested_utc)
            SELECT book_id, title, file_path, cover_path, added_utc FROM library_books WHERE book_id = '{book.Id:D}';
            """);
        var before = Snapshot(database);
        Assert.Empty(verifier.Verify().Results);
        Assert.Equal(before, Snapshot(database));
        Assert.True(File.Exists(book.FilePath));
    }

    private static (ListenShelfDatabase, SqliteAudiobookLibrary, SqliteManagedFileVerifier) Create(TestWorkspace workspace)
    {
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        return (database, new SqliteAudiobookLibrary(database, workspace.ManagedLibraryPath),
            new SqliteManagedFileVerifier(database, workspace.ManagedLibraryPath));
    }

    private static string[] Snapshot(ListenShelfDatabase database)
    {
        using var connection = database.OpenConnection();
        var rows = new List<string>();
        foreach (var table in new[] { "library_books", "playback_progress", "playback_bookmarks", "app_settings", "pending_library_removals", "schema_migrations" })
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {table} ORDER BY rowid;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(table + ":" + string.Join('|', Enumerable.Range(0, reader.FieldCount).Select(reader.GetValue)));
            }
        }
        return rows.ToArray();
    }

    private static void Execute(ListenShelfDatabase database, string sql)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
