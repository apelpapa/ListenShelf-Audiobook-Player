using System.Security.Cryptography;
using ListenShelf.Application.Library;
using ListenShelf.Infrastructure.Library;
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
}
