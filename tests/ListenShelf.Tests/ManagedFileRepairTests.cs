using ListenShelf.Application.Bookmarks;
using ListenShelf.Application.Library;
using ListenShelf.Application.Progress;
using ListenShelf.Desktop.ViewModels;
using ListenShelf.Infrastructure.Backup;
using ListenShelf.Infrastructure.Bookmarks;
using ListenShelf.Infrastructure.Library;
using ListenShelf.Infrastructure.Progress;
using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Tests;

public sealed class ManagedFileRepairTests
{
    [Fact]
    public void ChangedCopy_IsReplacedOnlyAfterVerification_AndAllCatalogDataSurvives()
    {
        using var workspace = new TestWorkspace();
        var (database, library, checker) = Create(workspace);
        byte[] original = [1, 2, 3, 4];
        byte[] damaged = [4, 3, 2, 1];
        var source = workspace.CreateSourceFile("Original 日本語.m4b", original);
        var sourceTime = File.GetLastWriteTimeUtc(source);
        var book = library.Import(source).Book;
        library.UpdateMetadata(book.Id, book.Metadata with { Title = "My edited title", Authors = ["An author"], SeriesName = "Series" });
        var cover = workspace.CreateSourceFile("cover.png", [5, 6, 7]);
        book = library.SetCover(book.Id, cover);
        var savedAt = DateTimeOffset.UtcNow;
        new SqlitePlaybackProgressStore(database).Save(new PlaybackProgress(book.FilePath, TimeSpan.FromMinutes(12), TimeSpan.FromHours(5), savedAt));
        new SqlitePlaybackBookmarkStore(database).Save(new PlaybackBookmark(Guid.NewGuid(), book.FilePath,
            TimeSpan.FromMinutes(4), "Keep bookmark", "Note", 2, "Chapter 3", savedAt, savedAt));
        File.WriteAllBytes(book.FilePath, damaged);
        var before = Snapshot(database);
        var stages = new List<LibraryImportStage>();

        var result = library.Repair(book.Id, source, new InlineProgress<LibraryImportProgress>(update =>
        {
            stages.Add(update.Stage);
            Assert.Equal(damaged, File.ReadAllBytes(book.FilePath));
        }));

        Assert.Equal(original, File.ReadAllBytes(book.FilePath));
        Assert.Equal(original, File.ReadAllBytes(source));
        Assert.Equal(sourceTime, File.GetLastWriteTimeUtc(source));
        Assert.Equal(damaged, File.ReadAllBytes(result.RetainedCopyPath!));
        Assert.Equal(book.Id, result.BookId);
        Assert.Equal(book.FilePath, result.FilePath);
        Assert.Equal(before, Snapshot(database));
        Assert.Equal(new byte[] { 5, 6, 7 }, File.ReadAllBytes(book.CoverPath!));
        Assert.Contains(LibraryImportStage.Copying, stages);
        Assert.Contains(LibraryImportStage.Verifying, stages);
        Assert.Equal(LibraryImportStage.Finalizing, stages[^1]);
        Assert.Equal(ManagedFileVerificationStatus.Unchanged,
            Assert.Single(new SqliteManagedFileVerifier(database, library.ManagedLibraryPath).Verify(book.Id).Results).Status);

        // Retained copies survive startup and are only removed by explicit cleanup.
        _ = new SqliteAudiobookLibrary(database, library.ManagedLibraryPath);
        var issue = Assert.Single(checker.Check().Issues);
        Assert.Equal(ManagedLibraryIntegrityIssueKind.RetainedRepairCopy, issue.Kind);
        var item = new ManagedStorageIssueItemViewModel(issue, _ => Task.CompletedTask, _ => Task.CompletedTask);
        Assert.False(item.CanRecover);
        Assert.True(item.CanCleanUp);
        Assert.False(item.IsCleanupConfirmationVisible);
        item.RequestCleanupCommand.Execute(null);
        Assert.True(item.IsCleanupConfirmationVisible);
        Assert.True(File.Exists(result.RetainedCopyPath));
        new SqliteManagedLibraryMaintenance(database, checker).CleanUp(result.RetainedCopyPath!);
        Assert.False(File.Exists(result.RetainedCopyPath));
        Assert.Equal(original, File.ReadAllBytes(book.FilePath));
        Assert.Equal(before, Snapshot(database));
        Assert.True(checker.Check().IsHealthy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingFileOrFolder_IsRestoredAtTheSamePath(bool missingFolder)
    {
        using var workspace = new TestWorkspace();
        var (database, library, checker) = Create(workspace);
        var source = workspace.CreateSourceFile("Book.mp3", [1, 2, 3]);
        var book = library.Import(source).Book;
        if (missingFolder) Directory.Delete(Path.GetDirectoryName(book.FilePath)!, recursive: true);
        else File.Delete(book.FilePath);
        var before = Snapshot(database);

        var result = library.Repair(book.Id, source);

        Assert.Null(result.RetainedCopyPath);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(book.FilePath));
        Assert.Equal(before, Snapshot(database));
        Assert.True(checker.Check().IsHealthy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WrongContentOrSize_DoesNotChangeAnyExistingFile(bool wrongSize)
    {
        using var workspace = new TestWorkspace();
        var (database, library, checker) = Create(workspace);
        var book = library.Import(workspace.CreateSourceFile("Book.m4a", [1, 2, 3])).Book;
        var wrong = workspace.CreateSourceFile("Different.m4a", wrongSize ? [9] : [3, 2, 1]);
        var before = Snapshot(database);

        Assert.Throws<IOException>(() => library.Repair(book.Id, wrong));

        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(book.FilePath));
        Assert.Equal(before, Snapshot(database));
        Assert.True(checker.Check().IsHealthy);
    }

    [Fact]
    public void NoBaseline_IsRefusedWithoutCreatingOne_AndSameTargetIsRefused()
    {
        using var workspace = new TestWorkspace();
        var (database, library, checker) = Create(workspace);
        var source = workspace.CreateSourceFile("Older.m4b", [1]);
        var book = library.Import(source).Book;
        Assert.Contains("separate known-good", Assert.Throws<IOException>(() => library.Repair(book.Id, book.FilePath)).Message);
        Execute(database, "UPDATE library_books SET content_sha256 = NULL;");
        var before = Snapshot(database);
        Assert.Contains("no saved fingerprint", Assert.Throws<InvalidOperationException>(() => library.Repair(book.Id, source)).Message);
        Assert.Equal(before, Snapshot(database));
        Assert.True(checker.Check().IsHealthy);
    }

    [Theory]
    [InlineData(LibraryImportStage.Copying)]
    [InlineData(LibraryImportStage.Verifying)]
    public void CancelDuringCopyOrVerification_RemovesOnlyAttemptFiles(LibraryImportStage cancelStage)
    {
        using var workspace = new TestWorkspace();
        using var cancellation = new CancellationTokenSource();
        var (database, library, checker) = Create(workspace);
        var bytes = new byte[3 * 1024 * 1024 + 5];
        var source = workspace.CreateSourceFile("Long.m4b", bytes);
        var book = library.Import(source).Book;
        File.WriteAllBytes(book.FilePath, [9]);
        var before = Snapshot(database);

        Assert.Throws<OperationCanceledException>(() => library.Repair(book.Id, source,
            new InlineProgress<LibraryImportProgress>(update =>
            {
                if (update.Stage == cancelStage && update.ProcessedBytes > 0) cancellation.Cancel();
            }), cancellation.Token));

        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(book.FilePath));
        Assert.Equal(bytes, File.ReadAllBytes(source));
        Assert.Equal(before, Snapshot(database));
        Assert.True(checker.Check().IsHealthy); // Structural check, not a fingerprint scan.
        Assert.Single(Directory.EnumerateFiles(Path.GetDirectoryName(book.FilePath)!));
    }

    [Fact]
    public void CancelBeforeStart_WritesNothing_AndLateCancelFinishesReplacement()
    {
        using var workspace = new TestWorkspace();
        var (_, library, _) = Create(workspace);
        var source = workspace.CreateSourceFile("Book.m4b", [1]);
        var book = library.Import(source).Book;
        File.Delete(book.FilePath);
        Assert.Throws<OperationCanceledException>(() => library.Repair(book.Id, source, cancellationToken: new CancellationToken(true)));
        Assert.False(File.Exists(book.FilePath));
        using var cancellation = new CancellationTokenSource();
        library.Repair(book.Id, source, new InlineProgress<LibraryImportProgress>(update =>
        {
            if (update.Stage == LibraryImportStage.Finalizing) cancellation.Cancel();
        }), cancellation.Token);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(book.FilePath));
    }

    [Fact]
    public void ReplacementFailure_PreservesVerifiedStageAndOriginal_ForExplicitRetry()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var workspace = new TestWorkspace();
        var (database, library, checker) = Create(workspace);
        var source = workspace.CreateSourceFile("Book.m4b", [1, 2]);
        var book = library.Import(source).Book;
        File.WriteAllBytes(book.FilePath, [9]);
        var before = Snapshot(database);
        using (var held = new FileStream(book.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Contains("Remaining repair files were kept", Assert.Throws<IOException>(() => library.Repair(book.Id, source)).Message);
        }

        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(book.FilePath));
        Assert.Equal(before, Snapshot(database));
        var issue = Assert.Single(checker.Check().Issues);
        Assert.Equal(ManagedLibraryIntegrityIssueKind.IncompleteRepairFile, issue.Kind);
        Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(issue.Path));
        // A verified interrupted stage can be supplied as a read-only repair source.
        library.Repair(book.Id, issue.Path);
        Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(book.FilePath));
        Assert.True(File.Exists(issue.Path));
    }

    [Fact]
    public void InterruptedWrite_CleansOnlyItsStage_AndKeepsTheStageExclusive()
    {
        using var workspace = new TestWorkspace();
        var (_, library, checker) = Create(workspace);
        var source = workspace.CreateSourceFile("Book.m4b", [1, 2, 3]);
        var book = library.Import(source).Book;
        var changed = false;
        Assert.Throws<IOException>(() => library.Repair(book.Id, source, new InlineProgress<LibraryImportProgress>(update =>
        {
            if (changed || update.Stage != LibraryImportStage.Copying || update.ProcessedBytes != 3) return;
            changed = true;
            // The writer is still exclusive, so verify the stage cannot be changed externally.
            var temporary = Assert.Single(Directory.EnumerateFiles(Path.GetDirectoryName(book.FilePath)!, "*.repairing"));
            Assert.Throws<IOException>(() => File.WriteAllBytes(temporary, [9, 9, 9]));
            throw new IOException("Simulated interrupted write");
        })));
        Assert.True(changed);
        Assert.True(checker.Check().IsHealthy);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(book.FilePath));
    }

    [Fact]
    public void UnsafeCatalogPath_IsRefused()
    {
        using var workspace = new TestWorkspace();
        var (database, library, _) = Create(workspace);
        var source = workspace.CreateSourceFile("Source.m4b", [1]);
        var book = library.Import(source).Book;
        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE library_books SET file_path = $path;";
            command.Parameters.AddWithValue("$path", source);
            command.ExecuteNonQuery();
        }
        Assert.Throws<IOException>(() => library.Repair(book.Id, source));
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(source));
    }

    [Fact]
    public void CatalogRemovedDuringCopy_IsNotRecreated()
    {
        using var workspace = new TestWorkspace();
        var (database, library, _) = Create(workspace);
        var source = workspace.CreateSourceFile("Source.m4b", [1, 2]);
        var book = library.Import(source).Book;
        Assert.Throws<KeyNotFoundException>(() => library.Repair(book.Id, source, new InlineProgress<LibraryImportProgress>(update =>
        {
            if (update.Stage == LibraryImportStage.Verifying) Execute(database, "DELETE FROM library_books;");
        })));
        Assert.Empty(library.GetBooks());
        Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(book.FilePath));
        Assert.Single(Directory.EnumerateFiles(Path.GetDirectoryName(book.FilePath)!));
    }

    [Fact]
    public void VerifiedCopyCannotBeRewrittenBeforeTheFinalSwap()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var workspace = new TestWorkspace();
        var (_, library, _) = Create(workspace);
        var source = workspace.CreateSourceFile("Book.m4b", [1, 2, 3]);
        var book = library.Import(source).Book;
        library.Repair(book.Id, source, new InlineProgress<LibraryImportProgress>(update =>
        {
            if (update.Stage != LibraryImportStage.Finalizing) return;
            var temporary = Assert.Single(Directory.EnumerateFiles(Path.GetDirectoryName(book.FilePath)!, "*.repairing"));
            Assert.Throws<IOException>(() => File.WriteAllBytes(temporary, [9, 9, 9]));
        }));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(book.FilePath));
    }

    [Fact]
    public void CancellationCleanupFailure_ReportsAndPreservesOnlyTheAttemptFile()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var workspace = new TestWorkspace();
        using var cancellation = new CancellationTokenSource();
        var (database, library, checker) = Create(workspace);
        var source = workspace.CreateSourceFile("Book.m4b", [1, 2, 3]);
        var book = library.Import(source).Book;
        FileStream? held = null;
        try
        {
            Assert.Contains("unfinished copy could not be removed", Assert.Throws<IOException>(() => library.Repair(book.Id, source,
                new InlineProgress<LibraryImportProgress>(update =>
                {
                    if (held is not null || update.Stage != LibraryImportStage.Verifying) return;
                    var temporary = Assert.Single(Directory.EnumerateFiles(Path.GetDirectoryName(book.FilePath)!, "*.repairing"));
                    held = new FileStream(temporary, FileMode.Open, FileAccess.Read, FileShare.Read);
                    cancellation.Cancel();
                }), cancellation.Token)).Message);
        }
        finally { held?.Dispose(); }
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(source));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(book.FilePath));
        var artifact = Assert.Single(checker.Check().Issues);
        Assert.Equal(ManagedLibraryIntegrityIssueKind.IncompleteRepairFile, artifact.Kind);
        new SqliteManagedLibraryMaintenance(database, checker).CleanUp(artifact.Path);
        Assert.True(checker.Check().IsHealthy);
    }

    [Fact]
    public async Task ConcurrentRepairWaitsForOtherLibraryWork_AndCanCancelWhileWaiting()
    {
        using var workspace = new TestWorkspace();
        var (_, library, _) = Create(workspace);
        var source = workspace.CreateSourceFile("Book.m4b", [1, 2, 3]);
        var book = library.Import(source).Book;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var first = Task.Run(() => library.Repair(book.Id, source, new InlineProgress<LibraryImportProgress>(update =>
        {
            if (update.Stage != LibraryImportStage.Finalizing) return;
            started.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
        })));
        Task? second = null;
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            second = Task.Run(() => library.Repair(book.Id, source, new InlineProgress<LibraryImportProgress>(update =>
            {
                if (update.Detail == "Waiting for another library operation to finish") waiting.TrySetResult();
            }), cancellation.Token));
            await waiting.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
            release.Set();
            await first.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Single(Directory.EnumerateFiles(Path.GetDirectoryName(book.FilePath)!, "*.repair-backup"));
        }
        finally
        {
            release.Set();
            cancellation.Cancel();
            await first.WaitAsync(TimeSpan.FromSeconds(10));
            if (second is not null)
            {
                try { await second.WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (OperationCanceledException) { }
            }
        }
    }

    [Fact]
    public void BackupIncludesRetainedRepairCopies()
    {
        using var workspace = new TestWorkspace();
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        var library = new SqliteAudiobookLibrary(database);
        var checker = new SqliteManagedLibraryIntegrityChecker(database, library.ManagedLibraryPath);
        var source = workspace.CreateSourceFile("Source.m4b", [1, 2]);
        var book = library.Import(source).Book;
        File.WriteAllBytes(book.FilePath, [9]);
        var result = library.Repair(book.Id, source);
        var backup = new ZipLibraryBackupService(database, checker);
        var backupPath = Path.Combine(Path.GetDirectoryName(source)!, "test.listenshelf-backup");
        backup.Export(backupPath);
        File.Delete(result.RetainedCopyPath!);

        backup.Restore(backupPath);

        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(result.RetainedCopyPath!));
        Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(book.FilePath));
    }

    private static (ListenShelfDatabase, SqliteAudiobookLibrary, SqliteManagedLibraryIntegrityChecker) Create(TestWorkspace workspace)
    {
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        var library = new SqliteAudiobookLibrary(database, workspace.ManagedLibraryPath);
        return (database, library, new SqliteManagedLibraryIntegrityChecker(database, library.ManagedLibraryPath));
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
            while (reader.Read()) rows.Add(table + ":" + string.Join('|', Enumerable.Range(0, reader.FieldCount).Select(reader.GetValue)));
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
