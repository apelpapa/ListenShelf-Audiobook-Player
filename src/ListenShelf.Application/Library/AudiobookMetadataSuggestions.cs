namespace ListenShelf.Application.Library;

public sealed record AudiobookMetadataSuggestions
{
    public IReadOnlyList<string> Authors { get; init; } = [];

    public IReadOnlyList<string> SeriesNames { get; init; } = [];

    public IReadOnlyList<string> OriginalPublishers { get; init; } = [];

    public IReadOnlyList<string> Genres { get; init; } = [];

    public IReadOnlyList<string> Narrators { get; init; } = [];

    public IReadOnlyList<string> AudioPublishers { get; init; } = [];

    public IReadOnlyList<string> Languages { get; init; } = [];

    public static AudiobookMetadataSuggestions FromBooks(IEnumerable<LibraryBook> books)
    {
        ArgumentNullException.ThrowIfNull(books);

        var metadata = books
            .Where(book => book.StorageMode == LibraryStorageMode.Managed)
            .Select(book => book.Metadata)
            .ToArray();

        return new AudiobookMetadataSuggestions
        {
            Authors = Normalize(metadata.SelectMany(item => item.Authors)),
            SeriesNames = Normalize(metadata.Select(item => item.SeriesName)),
            OriginalPublishers = Normalize(metadata.Select(item => item.OriginalPublisher)),
            Genres = Normalize(metadata.SelectMany(item => item.Genres)),
            Narrators = Normalize(metadata.SelectMany(item => item.Narrators)),
            AudioPublishers = Normalize(metadata.Select(item => item.AudioPublisher)),
            Languages = Normalize(metadata.Select(item => item.Language)),
        };
    }

    private static IReadOnlyList<string> Normalize(IEnumerable<string?> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
