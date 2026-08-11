using System.Globalization;
using ListenShelf.Application.Progress;
using ListenShelf.Application.Settings;

namespace ListenShelf.Application.Library;

public static class LibraryBookQuery
{
    public static LibraryStatusFilter GetStatus(PlaybackProgress? progress)
    {
        if (progress is null || progress.Position <= TimeSpan.Zero)
        {
            return LibraryStatusFilter.NotStarted;
        }

        return progress.Duration > TimeSpan.Zero && progress.Position >= progress.Duration
            ? LibraryStatusFilter.Finished
            : LibraryStatusFilter.InProgress;
    }

    public static double? GetProgressFraction(PlaybackProgress? progress)
    {
        if (progress is null || progress.Position <= TimeSpan.Zero)
        {
            return 0d;
        }

        return progress.Duration > TimeSpan.Zero
            ? Math.Clamp(progress.Position.TotalSeconds / progress.Duration.TotalSeconds, 0d, 1d)
            : null;
    }

    public static IReadOnlyList<T> Apply<T>(
        IEnumerable<T> items,
        Func<T, LibraryBook> bookSelector,
        Func<T, PlaybackProgress?> progressSelector,
        string? searchText,
        LibraryStatusFilter statusFilter,
        LibrarySortMode sortMode)
    {
        var matches = items
            .Select(item => new Entry<T>(item, bookSelector(item), progressSelector(item)))
            .Where(entry => LibraryBookSearch.Matches(entry.Book, searchText))
            .Where(entry => statusFilter == LibraryStatusFilter.All || GetStatus(entry.Progress) == statusFilter);

        var ordered = sortMode switch
        {
            LibrarySortMode.Author => matches
                .OrderBy(entry => Author(entry.Book) is null)
                .ThenBy(entry => Author(entry.Book), StringComparer.OrdinalIgnoreCase),
            LibrarySortMode.SeriesOrder => matches
                .OrderBy(entry => string.IsNullOrWhiteSpace(entry.Book.Metadata.SeriesName))
                .ThenBy(entry => entry.Book.Metadata.SeriesName?.Trim(), StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => SeriesPosition(entry.Book) is null)
                .ThenBy(entry => SeriesPosition(entry.Book)),
            LibrarySortMode.RecentlyPlayed => matches
                .OrderByDescending(entry => entry.Progress?.UpdatedAtUtc),
            LibrarySortMode.DateAdded => matches
                .OrderByDescending(entry => entry.Book.AddedAtUtc),
            LibrarySortMode.Progress => matches
                .OrderBy(entry => GetProgressFraction(entry.Progress) is null)
                .ThenByDescending(entry => GetProgressFraction(entry.Progress)),
            _ => matches.OrderBy(entry => entry.Book.Title.Trim(), StringComparer.OrdinalIgnoreCase),
        };

        return ordered
            .ThenBy(entry => entry.Book.Title.Trim(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Book.Id)
            .Select(entry => entry.Item)
            .ToArray();
    }

    private static string? Author(LibraryBook book) =>
        book.Metadata.Authors.FirstOrDefault(author => !string.IsNullOrWhiteSpace(author))?.Trim();

    private static decimal? SeriesPosition(LibraryBook book) =>
        decimal.TryParse(
            book.Metadata.SeriesPosition,
            NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite
                | NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var position)
                ? position
                : null;

    private sealed record Entry<T>(T Item, LibraryBook Book, PlaybackProgress? Progress);
}
