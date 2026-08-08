using System.Globalization;

namespace ListenShelf.Application.Library;

public static class LibraryBookSearch
{
    private static readonly char[] QuerySeparators = [' ', '\t', '\r', '\n'];

    public static bool Matches(LibraryBook book, string? query)
    {
        ArgumentNullException.ThrowIfNull(book);

        var terms = query?
            .Split(
                QuerySeparators,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [];
        if (terms.Length == 0)
        {
            return true;
        }

        var searchableValues = GetSearchableValues(book)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();

        return terms.All(term => searchableValues.Any(value =>
            value.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }

    private static IEnumerable<string?> GetSearchableValues(LibraryBook book)
    {
        var metadata = book.Metadata;

        yield return metadata.Title;
        yield return metadata.Subtitle;

        foreach (var author in metadata.Authors)
        {
            yield return author;
        }

        yield return metadata.SeriesName;
        yield return metadata.SeriesPosition;
        yield return metadata.OriginalPublicationYear?.ToString(CultureInfo.InvariantCulture);
        yield return metadata.OriginalPublisher;
        yield return metadata.Description;

        foreach (var genre in metadata.Genres)
        {
            yield return genre;
        }

        foreach (var narrator in metadata.Narrators)
        {
            yield return narrator;
        }

        yield return metadata.AudioPublisher;
        yield return metadata.AudiobookReleaseDate?.ToString(
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture);
        yield return metadata.Language;
        yield return metadata.Isbn10;
        yield return metadata.Isbn13;
        yield return metadata.Asin;
        yield return metadata.EditionName;
        yield return metadata.Abridgement.ToString();
        yield return metadata.EditionNotes;
        yield return Path.GetFileName(book.FilePath);
    }
}
