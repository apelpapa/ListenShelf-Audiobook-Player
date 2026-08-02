using System.Security.Cryptography;
using ListenShelf.Application.Bookmarks;
using ListenShelf.Application.Library;
using ListenShelf.Application.Progress;
using ListenShelf.Infrastructure.Bookmarks;
using ListenShelf.Infrastructure.Library;
using ListenShelf.Infrastructure.Progress;
using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Tests;

public sealed class AudiobookLibraryTests
{
    [Fact]
    public void Import_CreatesAnIdenticalManagedCopyWithoutChangingTheSource()
    {
        using var workspace = new TestWorkspace();
        var sourceContents = CreateDeterministicBytes(64 * 1024 + 17);
        var sourcePath = workspace.CreateSourceFile("Mý Audiobook 日本語.m4b", sourceContents);
        var sourceLastWriteUtc = new DateTime(2025, 3, 4, 5, 6, 8, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(sourcePath, sourceLastWriteUtc);

        var library = CreateLibrary(workspace);
        var result = library.Import(sourcePath);

        Assert.True(result.WasAdded);
        Assert.Equal("Mý Audiobook 日本語", result.Book.Title);
        Assert.True(File.Exists(result.Book.FilePath));
        Assert.False(PathsEqual(sourcePath, result.Book.FilePath));
        Assert.True(IsWithin(result.Book.FilePath, workspace.ManagedLibraryPath));
        Assert.Equal(sourceContents, File.ReadAllBytes(sourcePath));
        Assert.Equal(sourceContents, File.ReadAllBytes(result.Book.FilePath));
        Assert.Equal(
            SHA256.HashData(sourceContents),
            SHA256.HashData(File.ReadAllBytes(result.Book.FilePath)));
        Assert.Equal(sourceLastWriteUtc, File.GetLastWriteTimeUtc(sourcePath));
        Assert.DoesNotContain(
            Directory.EnumerateFiles(
                workspace.ManagedLibraryPath,
                "*.importing",
                SearchOption.AllDirectories),
            _ => true);

        var savedBook = Assert.Single(library.GetBooks());
        Assert.Equal(result.Book.Id, savedBook.Id);
        Assert.Equal(result.Book.FilePath, savedBook.FilePath);
    }

    [Fact]
    public void ImportingTheSameSourceTwice_ReusesTheExistingManagedBook()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = workspace.CreateSourceFile(
            "Duplicate Test.mp3",
            CreateDeterministicBytes(4096));
        var library = CreateLibrary(workspace);

        var first = library.Import(sourcePath);
        var second = library.Import(sourcePath);

        Assert.True(first.WasAdded);
        Assert.False(second.WasAdded);
        Assert.Equal(first.Book.Id, second.Book.Id);
        Assert.Equal(first.Book.FilePath, second.Book.FilePath);
        Assert.Single(library.GetBooks());
        Assert.Single(
            Directory.EnumerateFiles(
                workspace.ManagedLibraryPath,
                "*.mp3",
                SearchOption.AllDirectories));
    }

    [Fact]
    public void ImportingAnUnsupportedFile_LeavesTheLibraryEmpty()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = workspace.CreateSourceFile("Not an audiobook.wav", [1, 2, 3]);
        var library = CreateLibrary(workspace);

        var exception = Assert.Throws<NotSupportedException>(() => library.Import(sourcePath));

        Assert.Contains("M4B, M4A, and MP3", exception.Message, StringComparison.Ordinal);
        Assert.Empty(library.GetBooks());
        Assert.False(Directory.Exists(workspace.ManagedLibraryPath));
    }

    [Fact]
    public void MetadataAndCoverChanges_PersistWithoutChangingTheSelectedCoverFile()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = workspace.CreateSourceFile(
            "Metadata Test.m4a",
            CreateDeterministicBytes(8192));
        var coverContents = CreateDeterministicBytes(1024);
        var coverSourcePath = workspace.CreateSourceFile("cover.png", coverContents);
        var library = CreateLibrary(workspace);
        var importedBook = library.Import(sourcePath).Book;
        var metadata = new AudiobookMetadata
        {
            Title = "  The Test Book  ",
            Subtitle = "  A Subtitle  ",
            Authors = ["  Alice Author  ", "alice author", "Bob Writer"],
            SeriesName = "  Example Series  ",
            SeriesPosition = "  2  ",
            OriginalPublicationYear = 2024,
            OriginalPublisher = "  Example Books  ",
            Description = "  Description  ",
            Genres = ["Fantasy", " fantasy ", "Adventure"],
            Narrators = ["  Nora Narrator  "],
            AudioPublisher = "  Example Audio  ",
            AudiobookReleaseDate = new DateOnly(2025, 2, 3),
            Language = "  English  ",
            Isbn10 = "  1234567890  ",
            Isbn13 = "  1234567890123  ",
            Asin = "  B000TEST  ",
            EditionName = "  Unabridged Edition  ",
            Abridgement = AudiobookAbridgement.Unabridged,
            EditionNotes = "  Notes  ",
        };

        library.UpdateMetadata(importedBook.Id, metadata);
        var coveredBook = library.SetCover(importedBook.Id, coverSourcePath);
        var reloadedLibrary = CreateLibrary(workspace);
        var savedBook = Assert.Single(reloadedLibrary.GetBooks());

        Assert.Equal("The Test Book", savedBook.Title);
        Assert.Equal("A Subtitle", savedBook.Metadata.Subtitle);
        Assert.Equal(["Alice Author", "Bob Writer"], savedBook.Metadata.Authors);
        Assert.Equal("Example Series", savedBook.Metadata.SeriesName);
        Assert.Equal("2", savedBook.Metadata.SeriesPosition);
        Assert.Equal(2024, savedBook.Metadata.OriginalPublicationYear);
        Assert.Equal("Example Books", savedBook.Metadata.OriginalPublisher);
        Assert.Equal("Description", savedBook.Metadata.Description);
        Assert.Equal(["Fantasy", "Adventure"], savedBook.Metadata.Genres);
        Assert.Equal(["Nora Narrator"], savedBook.Metadata.Narrators);
        Assert.Equal("Example Audio", savedBook.Metadata.AudioPublisher);
        Assert.Equal(new DateOnly(2025, 2, 3), savedBook.Metadata.AudiobookReleaseDate);
        Assert.Equal("English", savedBook.Metadata.Language);
        Assert.Equal("1234567890", savedBook.Metadata.Isbn10);
        Assert.Equal("1234567890123", savedBook.Metadata.Isbn13);
        Assert.Equal("B000TEST", savedBook.Metadata.Asin);
        Assert.Equal("Unabridged Edition", savedBook.Metadata.EditionName);
        Assert.Equal(AudiobookAbridgement.Unabridged, savedBook.Metadata.Abridgement);
        Assert.Equal("Notes", savedBook.Metadata.EditionNotes);
        Assert.Equal(coveredBook.CoverPath, savedBook.CoverPath);
        Assert.NotNull(savedBook.CoverPath);
        Assert.Equal(coverContents, File.ReadAllBytes(coverSourcePath));
        Assert.Equal(coverContents, File.ReadAllBytes(savedBook.CoverPath));
    }

    [Fact]
    public void Remove_DeletesTheManagedBookAndAllCatalogedListeningData()
    {
        using var workspace = new TestWorkspace();
        var sourceContents = CreateDeterministicBytes(16 * 1024);
        var sourcePath = workspace.CreateSourceFile("Remove Me.m4b", sourceContents);
        var coverSourcePath = workspace.CreateSourceFile(
            "remove-cover.png",
            CreateDeterministicBytes(2048));
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        var library = new SqliteAudiobookLibrary(database, workspace.ManagedLibraryPath);
        var importedBook = library.Import(sourcePath).Book;
        var coveredBook = library.SetCover(importedBook.Id, coverSourcePath);
        var progressStore = new SqlitePlaybackProgressStore(database);
        var bookmarkStore = new SqlitePlaybackBookmarkStore(database);
        progressStore.Save(new PlaybackProgress(
            importedBook.FilePath,
            TimeSpan.FromMinutes(12),
            TimeSpan.FromHours(8),
            DateTimeOffset.UtcNow));
        bookmarkStore.Save(new PlaybackBookmark(
            Guid.NewGuid(),
            importedBook.FilePath,
            TimeSpan.FromMinutes(10),
            "Important",
            null,
            1,
            "Chapter 2",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));

        var result = library.Remove(importedBook.Id);

        Assert.Equal(importedBook.Title, result.Title);
        Assert.False(result.CleanupPending);
        Assert.Empty(library.GetBooks());
        Assert.False(File.Exists(importedBook.FilePath));
        Assert.False(Directory.Exists(Path.GetDirectoryName(importedBook.FilePath)));
        Assert.NotNull(coveredBook.CoverPath);
        Assert.False(File.Exists(coveredBook.CoverPath));
        Assert.Null(progressStore.Get(importedBook.FilePath));
        Assert.Empty(bookmarkStore.GetForFile(importedBook.FilePath));
        Assert.Equal(sourceContents, File.ReadAllBytes(sourcePath));
        Assert.True(File.Exists(coverSourcePath));

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pending_library_removals;";
        Assert.Equal(0L, (long)command.ExecuteScalar()!);
    }

    [Fact]
    public void Remove_RefusesToDeleteAFileOutsideTheManagedBookDirectory()
    {
        using var workspace = new TestWorkspace();
        var sourceContents = CreateDeterministicBytes(4096);
        var sourcePath = workspace.CreateSourceFile("Protected Original.mp3", sourceContents);
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        var library = new SqliteAudiobookLibrary(database, workspace.ManagedLibraryPath);
        var book = library.Import(sourcePath).Book;

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

        var exception = Assert.Throws<InvalidOperationException>(() => library.Remove(book.Id));

        Assert.Contains("outside its managed book directory", exception.Message, StringComparison.Ordinal);
        Assert.Equal(sourceContents, File.ReadAllBytes(sourcePath));
        Assert.Single(library.GetBooks());
    }

    [Fact]
    public void OpeningLibrary_CompletesAConfirmedRemovalInterruptedAfterStaging()
    {
        using var workspace = new TestWorkspace();
        var sourcePath = workspace.CreateSourceFile(
            "Interrupted Removal.m4a",
            CreateDeterministicBytes(4096));
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        var library = new SqliteAudiobookLibrary(database, workspace.ManagedLibraryPath);
        var book = library.Import(sourcePath).Book;
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

        var recoveredLibrary = new SqliteAudiobookLibrary(database, workspace.ManagedLibraryPath);

        Assert.Empty(recoveredLibrary.GetBooks());
        Assert.False(Directory.Exists(stagedDirectory));
        Assert.True(File.Exists(sourcePath));
        using var verificationConnection = database.OpenConnection();
        using var verificationCommand = verificationConnection.CreateCommand();
        verificationCommand.CommandText = "SELECT COUNT(*) FROM pending_library_removals;";
        Assert.Equal(0L, (long)verificationCommand.ExecuteScalar()!);
    }

    private static SqliteAudiobookLibrary CreateLibrary(TestWorkspace workspace) =>
        new(
            new ListenShelfDatabase(workspace.DatabasePath),
            workspace.ManagedLibraryPath);

    private static byte[] CreateDeterministicBytes(int length) =>
        Enumerable.Range(0, length)
            .Select(index => (byte)(index % 251))
            .ToArray();

    private static bool PathsEqual(string firstPath, string secondPath) =>
        string.Equals(
            Path.GetFullPath(firstPath),
            Path.GetFullPath(secondPath),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    private static bool IsWithin(string filePath, string directoryPath)
    {
        var normalizedDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(directoryPath)) + Path.DirectorySeparatorChar;

        return Path.GetFullPath(filePath).StartsWith(
            normalizedDirectory,
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }

    private static string CreatePathKey(string path) =>
        OperatingSystem.IsWindows()
            ? Path.GetFullPath(path).ToUpperInvariant()
            : Path.GetFullPath(path);
}
