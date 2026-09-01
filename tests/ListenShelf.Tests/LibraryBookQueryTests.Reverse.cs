using ListenShelf.Application.Settings;

namespace ListenShelf.Tests;

public sealed partial class LibraryBookQueryTests
{
    [Theory]
    [InlineData(LibrarySortMode.Title)]
    [InlineData(LibrarySortMode.Author)]
    [InlineData(LibrarySortMode.SeriesOrder)]
    [InlineData(LibrarySortMode.RecentlyPlayed)]
    [InlineData(LibrarySortMode.DateAdded)]
    [InlineData(LibrarySortMode.Progress)]
    public void Reverse_InvertsCompleteDeterministicOrderWithoutMutatingInput(LibrarySortMode mode)
    {
        var tied = Entry("Same", "Same", "Series", "2.5", progress: Progress(10, 100));
        var books = new[]
        {
            tied,
            tied with { Book = tied.Book with { Id = Guid.NewGuid() } },
            Entry("Alpha", "Bob", "Series", "2", 3, Progress(50, 100, 2)),
            Entry("Beta", "Alice", "Series", "10", 2, Progress(100, 100, 3)),
            Entry("No metadata"),
            Entry("Unknown", "Alice", "Series", "bonus", progress: Progress(10, 0)),
        };
        var original = books.ToArray();
        var normal = Query(books, mode);
        var reversed = Query(books, mode, reverse: true);

        Assert.Equal(normal.Reverse(), reversed);
        Assert.Equal(reversed, Query(books.Reverse(), mode, reverse: true));
        Assert.Equal(normal, Query(books, mode));
        Assert.Equal(original, books);
        Assert.All(reversed, item => Assert.Contains(books, originalItem => ReferenceEquals(originalItem, item)));
    }

    [Fact]
    public void Reverse_TitleAndNumericSeriesRunFromLastToFirst()
    {
        var books = new[]
        {
            Entry("One", series: "Carl", number: "1"),
            Entry("Ten", series: "Carl", number: "10"),
            Entry("Two", series: "Carl", number: "2"),
            Entry("Unknown", series: "Carl", number: "bonus"),
            Entry("No series"),
        };
        Assert.Equal(["Unknown", "Two", "Ten", "One", "No series"], Titles(Query(books, LibrarySortMode.Title, reverse: true)));
        Assert.Equal(["No series", "Unknown", "Ten", "Two", "One"], Titles(Query(books, LibrarySortMode.SeriesOrder, reverse: true)));
    }

    [Fact]
    public void Reverse_OnlyReordersMatchingBooksAndHandlesEmptyOrSingleResults()
    {
        var books = new[]
        {
            Entry("Alpha", "Matt", progress: Progress(10, 100)),
            Entry("Beta", "Matt", progress: Progress(20, 100)),
            Entry("Charlie", "Jane", progress: Progress(50, 100)),
            Entry("Delta", "Matt", progress: Progress(100, 100)),
        };
        Assert.Equal(["Beta", "Alpha"], Titles(Query(books, LibrarySortMode.Title, LibraryStatusFilter.InProgress, "mAtT", reverse: true)));
        Assert.Empty(Query(books, LibrarySortMode.Title, search: "no match", reverse: true));
        Assert.Equal(["Delta"], Titles(Query(books, LibrarySortMode.Title, LibraryStatusFilter.Finished, reverse: true)));
        Assert.Empty(Query([], LibrarySortMode.Title, reverse: true));
    }
}
