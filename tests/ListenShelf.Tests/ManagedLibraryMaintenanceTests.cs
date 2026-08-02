using ListenShelf.Application.Library;
using ListenShelf.Infrastructure.Library;
using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Tests;

public sealed class ManagedLibraryMaintenanceTests
{
    [Fact]
    public void RecoverAudiobook_AdoptsACompleteOrphanedBookDirectory()
    {
        using var workspace = new TestWorkspace();
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        var bookId = Guid.NewGuid();
        var orphanDirectory = Path.Combine(
            workspace.ManagedLibraryPath,
            bookId.ToString("N"));
        Directory.CreateDirectory(orphanDirectory);
        var orphanPath = Path.Combine(orphanDirectory, "Recovered Story.m4b");
        var contents = new byte[] { 1, 2, 3, 4, 5 };
        File.WriteAllBytes(orphanPath, contents);
        var checker = new SqliteManagedLibraryIntegrityChecker(
            database,
            workspace.ManagedLibraryPath);
        var maintenance = new SqliteManagedLibraryMaintenance(database, checker);

        var result = maintenance.RecoverAudiobook(orphanPath);

        Assert.Equal(bookId, result.Book.Id);
        Assert.False(result.OrphanCleanupPending);
        Assert.True(PathsEqual(orphanPath, result.Book.FilePath));
        Assert.Equal(contents, File.ReadAllBytes(result.Book.FilePath));
        var catalogBook = Assert.Single(
            new SqliteAudiobookLibrary(database, workspace.ManagedLibraryPath).GetBooks());
        Assert.Equal(result.Book.Id, catalogBook.Id);
        Assert.True(PathsEqual(result.Book.FilePath, catalogBook.FilePath));
        Assert.True(checker.Check().IsHealthy);
    }

    [Fact]
    public void RecoverAudiobook_VerifiesANewManagedCopyAndRemovesTheLooseOrphan()
    {
        using var workspace = new TestWorkspace();
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        Directory.CreateDirectory(workspace.ManagedLibraryPath);
        var orphanPath = Path.Combine(workspace.ManagedLibraryPath, "Loose Story.mp3");
        var contents = new byte[] { 10, 20, 30, 40, 50, 60 };
        File.WriteAllBytes(orphanPath, contents);
        var checker = new SqliteManagedLibraryIntegrityChecker(
            database,
            workspace.ManagedLibraryPath);
        var maintenance = new SqliteManagedLibraryMaintenance(database, checker);

        var result = maintenance.RecoverAudiobook(orphanPath);

        Assert.False(result.OrphanCleanupPending);
        Assert.False(File.Exists(orphanPath));
        Assert.True(File.Exists(result.Book.FilePath));
        Assert.False(PathsEqual(orphanPath, result.Book.FilePath));
        Assert.Equal(contents, File.ReadAllBytes(result.Book.FilePath));
        Assert.True(checker.Check().IsHealthy);
    }

    [Fact]
    public void RecoverAudiobook_PreservesAnExistingBookWhenItsFolderContainsAnotherAudiobook()
    {
        using var workspace = new TestWorkspace();
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        var library = new SqliteAudiobookLibrary(database, workspace.ManagedLibraryPath);
        var existingBook = library.Import(
            workspace.CreateSourceFile("Existing Book.m4b", [1, 2, 3, 4])).Book;
        var orphanPath = Path.Combine(
            Path.GetDirectoryName(existingBook.FilePath)!,
            "Bonus Story.mp3");
        var orphanContents = new byte[] { 9, 8, 7, 6 };
        File.WriteAllBytes(orphanPath, orphanContents);
        var checker = new SqliteManagedLibraryIntegrityChecker(
            database,
            workspace.ManagedLibraryPath);
        var maintenance = new SqliteManagedLibraryMaintenance(database, checker);

        var result = maintenance.RecoverAudiobook(orphanPath);

        Assert.NotEqual(existingBook.Id, result.Book.Id);
        Assert.False(File.Exists(orphanPath));
        Assert.Equal(orphanContents, File.ReadAllBytes(result.Book.FilePath));
        Assert.Equal(2, library.GetBooks().Count);
        Assert.True(File.Exists(existingBook.FilePath));
        Assert.True(checker.Check().IsHealthy);
    }

    [Fact]
    public void CleanUp_DeletesOnlyTheExplicitlySelectedOrphanedFolder()
    {
        using var workspace = new TestWorkspace();
        var sourceContents = new byte[] { 7, 8, 9, 10 };
        var sourcePath = workspace.CreateSourceFile("Protected Original.m4a", sourceContents);
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        var library = new SqliteAudiobookLibrary(database, workspace.ManagedLibraryPath);
        var managedBook = library.Import(sourcePath).Book;
        var orphanDirectory = Path.Combine(workspace.ManagedLibraryPath, "old-import");
        var nestedDirectory = Path.Combine(orphanDirectory, "nested");
        Directory.CreateDirectory(nestedDirectory);
        File.WriteAllBytes(Path.Combine(orphanDirectory, "orphaned.m4b"), [1, 2, 3]);
        File.WriteAllText(Path.Combine(nestedDirectory, "notes.txt"), "unused");
        var checker = new SqliteManagedLibraryIntegrityChecker(
            database,
            workspace.ManagedLibraryPath);
        var maintenance = new SqliteManagedLibraryMaintenance(database, checker);

        var result = maintenance.CleanUp(orphanDirectory);

        Assert.True(result.WasDirectory);
        Assert.False(Directory.Exists(orphanDirectory));
        Assert.True(File.Exists(managedBook.FilePath));
        Assert.Equal(sourceContents, File.ReadAllBytes(sourcePath));
        Assert.True(checker.Check().IsHealthy);
    }

    [Fact]
    public void CleanUp_DeletesAConfirmedStaleImportFile()
    {
        using var workspace = new TestWorkspace();
        var now = new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        Directory.CreateDirectory(workspace.ManagedLibraryPath);
        var staleImportPath = Path.Combine(
            workspace.ManagedLibraryPath,
            "unfinished.m4b.importing");
        File.WriteAllBytes(staleImportPath, [1, 2, 3]);
        File.SetLastWriteTimeUtc(
            staleImportPath,
            now.UtcDateTime - TimeSpan.FromHours(25));
        var checker = new SqliteManagedLibraryIntegrityChecker(
            database,
            workspace.ManagedLibraryPath,
            new FixedTimeProvider(now));
        var maintenance = new SqliteManagedLibraryMaintenance(database, checker);

        var result = maintenance.CleanUp(staleImportPath);

        Assert.False(result.WasDirectory);
        Assert.False(File.Exists(staleImportPath));
        Assert.True(checker.Check().IsHealthy);
    }

    [Fact]
    public void CleanUp_RefusesToDeleteACatalogedManagedAudiobook()
    {
        using var workspace = new TestWorkspace();
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        var library = new SqliteAudiobookLibrary(database, workspace.ManagedLibraryPath);
        var book = library.Import(
            workspace.CreateSourceFile("Cataloged Book.m4b", [1, 2, 3, 4])).Book;
        var checker = new SqliteManagedLibraryIntegrityChecker(
            database,
            workspace.ManagedLibraryPath);
        var maintenance = new SqliteManagedLibraryMaintenance(database, checker);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            maintenance.CleanUp(book.FilePath));

        Assert.Contains("no longer reported", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(book.FilePath));
        Assert.Single(library.GetBooks());
    }

    [Fact]
    public void CleanUp_RefusesToDeleteAFileOutsideManagedStorage()
    {
        using var workspace = new TestWorkspace();
        var protectedPath = workspace.CreateSourceFile(
            "Never A Cleanup Target.mp3",
            [5, 4, 3, 2, 1]);
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        var checker = new SqliteManagedLibraryIntegrityChecker(
            database,
            workspace.ManagedLibraryPath);
        var maintenance = new SqliteManagedLibraryMaintenance(database, checker);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            maintenance.CleanUp(protectedPath));

        Assert.Contains("inside its managed library", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(protectedPath));
    }

    private static bool PathsEqual(string firstPath, string secondPath) =>
        string.Equals(
            Path.GetFullPath(firstPath),
            Path.GetFullPath(secondPath),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
