using ListenShelf.Application.Library;

namespace ListenShelf.Tests;

public sealed class ApplicationLibraryTests
{
    [Theory]
    [InlineData("book.m4b")]
    [InlineData("BOOK.M4A")]
    [InlineData(@"C:\Audiobooks\book.MP3")]
    public void SupportedAudiobookExtensions_AreAccepted(string filePath)
    {
        Assert.True(AudiobookFileFormats.IsSupported(filePath));
    }

    [Theory]
    [InlineData("")]
    [InlineData("book.wav")]
    [InlineData("book")]
    public void UnsupportedAudiobookExtensions_AreRejected(string filePath)
    {
        Assert.False(AudiobookFileFormats.IsSupported(filePath));
    }

    [Fact]
    public void MetadataSuggestions_AreDeduplicatedAndAlphabetized()
    {
        var books = new[]
        {
            CreateBook(new AudiobookMetadata
            {
                Title = "First",
                Authors = ["Zed Author", "alice author"],
                SeriesName = "Zeta Series",
                Narrators = ["Narrator B"],
                Genres = ["Fantasy"],
                OriginalPublisher = "Publisher B",
                AudioPublisher = "Audio B",
                Language = "English",
            }),
            CreateBook(new AudiobookMetadata
            {
                Title = "Second",
                Authors = ["Alice Author", "Bob Author"],
                SeriesName = "Alpha Series",
                Narrators = ["Narrator A"],
                Genres = ["fantasy", "Adventure"],
                OriginalPublisher = "Publisher A",
                AudioPublisher = "Audio A",
                Language = "French",
            }),
        };

        var suggestions = AudiobookMetadataSuggestions.FromBooks(books);

        Assert.Equal(["alice author", "Bob Author", "Zed Author"], suggestions.Authors);
        Assert.Equal(["Alpha Series", "Zeta Series"], suggestions.SeriesNames);
        Assert.Equal(["Adventure", "Fantasy"], suggestions.Genres);
        Assert.Equal(["Narrator A", "Narrator B"], suggestions.Narrators);
        Assert.Equal(["Publisher A", "Publisher B"], suggestions.OriginalPublishers);
        Assert.Equal(["Audio A", "Audio B"], suggestions.AudioPublishers);
        Assert.Equal(["English", "French"], suggestions.Languages);
    }

    private static LibraryBook CreateBook(AudiobookMetadata metadata) =>
        new(
            Guid.NewGuid(),
            metadata,
            Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.m4b"),
            1,
            DateTimeOffset.UtcNow);
}
