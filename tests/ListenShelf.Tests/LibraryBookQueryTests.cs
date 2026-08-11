using ListenShelf.Application.Library;
using ListenShelf.Application.Progress;
using ListenShelf.Application.Settings;

namespace ListenShelf.Tests;

public sealed class LibraryBookQueryTests
{
    [Theory]
    [InlineData(0, 100, LibraryStatusFilter.NotStarted)]
    [InlineData(-1, 100, LibraryStatusFilter.NotStarted)]
    [InlineData(50, 100, LibraryStatusFilter.InProgress)]
    [InlineData(99, 100, LibraryStatusFilter.InProgress)]
    [InlineData(100, 100, LibraryStatusFilter.Finished)]
    [InlineData(120, 100, LibraryStatusFilter.Finished)]
    [InlineData(50, 0, LibraryStatusFilter.InProgress)]
    [InlineData(0, 0, LibraryStatusFilter.NotStarted)]
    public void Status_UsesPositionAndRequiresKnownDurationToFinish(
        int position, int duration, LibraryStatusFilter expected)
    {
        Assert.Equal(expected, LibraryBookQuery.GetStatus(Progress(position, duration)));
    }

    [Fact]
    public void MissingProgress_IsNotStarted()
    {
        Assert.Equal(LibraryStatusFilter.NotStarted, LibraryBookQuery.GetStatus(null));
        Assert.Equal(0d, LibraryBookQuery.GetProgressFraction(null));
    }

    [Fact]
    public void SearchAndStatus_CombineBeforeSorting()
    {
        var books = new[]
        {
            Entry("Bravo", "Matt", progress: Progress(30, 100)),
            Entry("Alpha", "Matt", progress: Progress(10, 100)),
            Entry("Finished", "Matt", progress: Progress(100, 100)),
            Entry("Unstarted", "Matt"),
            Entry("Another author", "Jane", progress: Progress(10, 100)),
        };

        Assert.Equal(["Alpha", "Bravo"], Titles(Query(
            books, LibrarySortMode.Title, LibraryStatusFilter.InProgress, "mAtT")));
        Assert.Empty(Query(books, LibrarySortMode.Title, LibraryStatusFilter.InProgress, "Finished"));
        Assert.Single(Query(books, LibrarySortMode.Title, LibraryStatusFilter.Finished, "Matt"));
        Assert.Single(Query(books, LibrarySortMode.Title, LibraryStatusFilter.NotStarted));
    }

    [Fact]
    public void TitleAndAuthor_AreCaseInsensitiveWithUnknownAuthorsLast()
    {
        var books = new[]
        {
            Entry("charlie", "Zebra"),
            Entry("Beta", " alice "),
            Entry("alpha"),
            Entry("Delta", "ALICE"),
        };

        Assert.Equal(["alpha", "Beta", "charlie", "Delta"], Titles(Query(books, LibrarySortMode.Title)));
        Assert.Equal(["Beta", "Delta", "charlie", "alpha"], Titles(Query(books, LibrarySortMode.Author)));
    }

    [Fact]
    public void SeriesOrder_SortsNamesThenNumericPositionsAndPutsMissingMetadataLast()
    {
        var books = new[]
        {
            Entry("Ten", series: "Carl", number: "10"),
            Entry("No series"),
            Entry("Unknown position", series: "Carl", number: "bonus"),
            Entry("Second series", series: "Dune", number: "1"),
            Entry("Two and a half", series: "carl", number: "2.5"),
            Entry("Two", series: " Carl ", number: "2"),
            Entry("One", series: "Carl", number: "1"),
        };

        Assert.Equal(
            ["One", "Two", "Two and a half", "Ten", "Unknown position", "Second series", "No series"],
            Titles(Query(books, LibrarySortMode.SeriesOrder)));
    }

    [Fact]
    public void RecentAndDateAdded_AreNewestFirstWithUnplayedBooksLast()
    {
        var books = new[]
        {
            Entry("New import", addedDay: 3),
            Entry("Last played", addedDay: 1, progress: Progress(0, 100, day: 5)),
            Entry("Earlier played", addedDay: 2, progress: Progress(50, 100, day: 4)),
        };

        Assert.Equal(["Last played", "Earlier played", "New import"], Titles(Query(books, LibrarySortMode.RecentlyPlayed)));
        Assert.Equal(["New import", "Earlier played", "Last played"], Titles(Query(books, LibrarySortMode.DateAdded)));
    }

    [Fact]
    public void Progress_UsesPercentageNotElapsedTimeAndHandlesUnknownDurations()
    {
        var books = new[]
        {
            Entry("Long book", progress: Progress(900, 10000)),
            Entry("Short book", progress: Progress(90, 100)),
            Entry("Finished", progress: Progress(110, 100)),
            Entry("Unstarted"),
            Entry("Unknown duration", progress: Progress(100, 0)),
        };

        Assert.Equal(
            ["Finished", "Short book", "Long book", "Unstarted", "Unknown duration"],
            Titles(Query(books, LibrarySortMode.Progress)));
        Assert.Null(LibraryBookQuery.GetProgressFraction(books[4].Progress));
        Assert.Equal(1d, LibraryBookQuery.GetProgressFraction(books[2].Progress));
    }

    [Fact]
    public void Ties_KeepDeterministicOrderRegardlessOfInputOrder()
    {
        var first = Entry("Same") with
        {
            Book = Entry("Same").Book with { Id = Guid.Parse("00000000-0000-0000-0000-000000000001") },
        };
        var second = first with
        {
            Book = first.Book with { Id = Guid.Parse("00000000-0000-0000-0000-000000000002") },
        };

        foreach (var mode in Enum.GetValues<LibrarySortMode>())
        {
            Assert.Equal([first, second], Query([second, first], mode));
        }
    }

    private static IReadOnlyList<TestEntry> Query(
        IEnumerable<TestEntry> entries,
        LibrarySortMode sort,
        LibraryStatusFilter filter = LibraryStatusFilter.All,
        string? search = null) => LibraryBookQuery.Apply(
            entries, entry => entry.Book, entry => entry.Progress, search, filter, sort);

    private static string[] Titles(IEnumerable<TestEntry> entries) =>
        entries.Select(entry => entry.Book.Title).ToArray();

    private static TestEntry Entry(
        string title, string? author = null, string? series = null, string? number = null,
        int addedDay = 1, PlaybackProgress? progress = null) => new(
            new LibraryBook(
                Guid.NewGuid(),
                new AudiobookMetadata
                {
                    Title = title,
                    Authors = author is null ? [] : [author],
                    SeriesName = series,
                    SeriesPosition = number,
                },
                $"{title}.m4b", 100,
                new DateTimeOffset(2026, 9, addedDay, 0, 0, 0, TimeSpan.Zero)), progress);

    private static PlaybackProgress Progress(int position, int duration, int day = 1) => new(
        "test.m4b", TimeSpan.FromSeconds(position), TimeSpan.FromSeconds(duration),
        new DateTimeOffset(2026, 9, day, 0, 0, 0, TimeSpan.Zero));

    private sealed record TestEntry(LibraryBook Book, PlaybackProgress? Progress);
}
