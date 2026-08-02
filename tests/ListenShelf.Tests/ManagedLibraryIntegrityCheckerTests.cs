using ListenShelf.Application.Library;
using ListenShelf.Infrastructure.Library;
using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Tests;

public sealed class ManagedLibraryIntegrityCheckerTests
{
    [Fact]
    public void Check_ReportsAHealthyManagedLibrary()
    {
        using var workspace = new TestWorkspace();
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        var library = new SqliteAudiobookLibrary(database, workspace.ManagedLibraryPath);
        library.Import(workspace.CreateSourceFile("Healthy Book.m4b", [1, 2, 3, 4]));
        var checker = new SqliteManagedLibraryIntegrityChecker(
            database,
            workspace.ManagedLibraryPath);

        var report = checker.Check();

        Assert.True(report.IsHealthy);
        Assert.Equal(1, report.CatalogBookCount);
        Assert.Equal(1, report.ReferencedManagedFileCount);
        Assert.Empty(report.Issues);
    }

    [Fact]
    public void Check_DetectsMissingOrphanedAndStaleManagedStorage()
    {
        using var workspace = new TestWorkspace();
        var now = new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        var library = new SqliteAudiobookLibrary(database, workspace.ManagedLibraryPath);
        var book = library.Import(
            workspace.CreateSourceFile("Damaged Book.mp3", [1, 2, 3, 4])).Book;
        var bookDirectory = Path.GetDirectoryName(book.FilePath)!;
        File.Delete(book.FilePath);

        var unexpectedFile = Path.Combine(bookDirectory, "unexpected.txt");
        File.WriteAllText(unexpectedFile, "unexpected");
        var staleImportFile = Path.Combine(bookDirectory, "abandoned.m4b.importing");
        File.WriteAllBytes(staleImportFile, [5, 6, 7]);
        File.SetLastWriteTimeUtc(staleImportFile, now.UtcDateTime - TimeSpan.FromHours(25));
        var recentImportFile = Path.Combine(bookDirectory, "active.m4b.importing");
        File.WriteAllBytes(recentImportFile, [8, 9]);
        File.SetLastWriteTimeUtc(recentImportFile, now.UtcDateTime - TimeSpan.FromMinutes(5));

        var orphanDirectory = Path.Combine(workspace.ManagedLibraryPath, "orphaned-book");
        Directory.CreateDirectory(orphanDirectory);
        var orphanAudiobook = Path.Combine(orphanDirectory, "orphaned.m4b");
        File.WriteAllBytes(orphanAudiobook, [10, 11, 12]);
        var nestedOrphanDirectory = Path.Combine(orphanDirectory, "nested");
        Directory.CreateDirectory(nestedOrphanDirectory);
        var nestedOrphanAudiobook = Path.Combine(nestedOrphanDirectory, "nested.mp3");
        File.WriteAllBytes(nestedOrphanAudiobook, [15, 16, 17]);
        var orphanRootFile = Path.Combine(workspace.ManagedLibraryPath, "loose.mp3");
        File.WriteAllBytes(orphanRootFile, [13, 14]);

        var checker = new SqliteManagedLibraryIntegrityChecker(
            database,
            workspace.ManagedLibraryPath,
            new FixedTimeProvider(now));

        var report = checker.Check();

        Assert.False(report.IsHealthy);
        Assert.Equal(1, report.CatalogBookCount);
        Assert.Equal(0, report.ReferencedManagedFileCount);
        Assert.Contains(
            report.Issues,
            issue => issue.Kind == ManagedLibraryIntegrityIssueKind.MissingManagedFile
                     && PathsEqual(issue.Path, book.FilePath));
        Assert.Contains(
            report.Issues,
            issue => issue.Kind == ManagedLibraryIntegrityIssueKind.UnreferencedDirectory
                     && PathsEqual(issue.Path, orphanDirectory));
        Assert.Contains(
            report.Issues,
            issue => issue.Kind == ManagedLibraryIntegrityIssueKind.UnreferencedFile
                     && PathsEqual(issue.Path, unexpectedFile));
        Assert.Contains(
            report.Issues,
            issue => issue.Kind == ManagedLibraryIntegrityIssueKind.UnreferencedFile
                     && PathsEqual(issue.Path, orphanAudiobook));
        Assert.Contains(
            report.Issues,
            issue => issue.Kind == ManagedLibraryIntegrityIssueKind.UnreferencedDirectory
                     && PathsEqual(issue.Path, nestedOrphanDirectory));
        Assert.Contains(
            report.Issues,
            issue => issue.Kind == ManagedLibraryIntegrityIssueKind.UnreferencedFile
                     && PathsEqual(issue.Path, nestedOrphanAudiobook));
        Assert.Contains(
            report.Issues,
            issue => issue.Kind == ManagedLibraryIntegrityIssueKind.UnreferencedFile
                     && PathsEqual(issue.Path, orphanRootFile));
        Assert.Contains(
            report.Issues,
            issue => issue.Kind == ManagedLibraryIntegrityIssueKind.StaleImportFile
                     && PathsEqual(issue.Path, staleImportFile));
        Assert.DoesNotContain(
            report.Issues,
            issue => PathsEqual(issue.Path, recentImportFile));
    }

    [Fact]
    public void Check_ReportsJournaledRemovalStagingAsPendingCleanup()
    {
        using var workspace = new TestWorkspace();
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        var library = new SqliteAudiobookLibrary(database, workspace.ManagedLibraryPath);
        var book = library.Import(
            workspace.CreateSourceFile("Pending Removal.m4a", [1, 2, 3, 4])).Book;
        var bookDirectory = Path.GetDirectoryName(book.FilePath)!;
        var stagedDirectory = Path.Combine(
            workspace.ManagedLibraryPath,
            ".removing",
            book.Id.ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(stagedDirectory)!);
        Directory.Move(bookDirectory, stagedDirectory);

        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                INSERT INTO pending_library_removals (
                    book_id,
                    title,
                    file_path,
                    cover_path,
                    requested_utc)
                VALUES (
                    $book_id,
                    $title,
                    $file_path,
                    NULL,
                    $requested_utc);
                """;
            command.Parameters.AddWithValue("$book_id", book.Id.ToString("D"));
            command.Parameters.AddWithValue("$title", book.Title);
            command.Parameters.AddWithValue("$file_path", book.FilePath);
            command.Parameters.AddWithValue("$requested_utc", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }

        var checker = new SqliteManagedLibraryIntegrityChecker(
            database,
            workspace.ManagedLibraryPath);

        var report = checker.Check();

        Assert.False(report.IsHealthy);
        Assert.Equal(0, report.CatalogBookCount);
        var issue = Assert.Single(report.Issues);
        Assert.Equal(ManagedLibraryIntegrityIssueKind.PendingRemovalCleanup, issue.Kind);
        Assert.Equal(book.Id, issue.BookId);
        Assert.True(PathsEqual(stagedDirectory, issue.Path));
        Assert.True(Directory.Exists(stagedDirectory));
    }

    [Fact]
    public void Check_FlagsACatalogPathOutsideListenShelfWithoutChangingEitherFile()
    {
        using var workspace = new TestWorkspace();
        var originalContents = new byte[] { 1, 2, 3, 4, 5 };
        var sourcePath = workspace.CreateSourceFile("Protected Source.m4b", originalContents);
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        var library = new SqliteAudiobookLibrary(database, workspace.ManagedLibraryPath);
        var book = library.Import(sourcePath).Book;
        var managedContents = File.ReadAllBytes(book.FilePath);

        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                UPDATE library_books
                SET file_path = $file_path,
                    file_key = $file_key
                WHERE book_id = $book_id;
                """;
            command.Parameters.AddWithValue("$file_path", sourcePath);
            command.Parameters.AddWithValue("$file_key", CreatePathKey(sourcePath));
            command.Parameters.AddWithValue("$book_id", book.Id.ToString("D"));
            command.ExecuteNonQuery();
        }

        var checker = new SqliteManagedLibraryIntegrityChecker(
            database,
            workspace.ManagedLibraryPath);
        var report = checker.Check();

        Assert.Contains(
            report.Issues,
            issue => issue.Kind == ManagedLibraryIntegrityIssueKind.CatalogPathOutsideManagedStorage
                     && PathsEqual(issue.Path, sourcePath));
        Assert.Equal(originalContents, File.ReadAllBytes(sourcePath));
        Assert.Equal(managedContents, File.ReadAllBytes(book.FilePath));
    }

    private static bool PathsEqual(string firstPath, string secondPath) =>
        string.Equals(
            Path.GetFullPath(firstPath),
            Path.GetFullPath(secondPath),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    private static string CreatePathKey(string path) =>
        OperatingSystem.IsWindows()
            ? Path.GetFullPath(path).ToUpperInvariant()
            : Path.GetFullPath(path);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
