namespace ListenShelf.Application.Library;

public sealed record AudiobookMetadata
{
    public required string Title { get; init; }

    public string? Subtitle { get; init; }

    public IReadOnlyList<string> Authors { get; init; } = [];

    public string? SeriesName { get; init; }

    public string? SeriesPosition { get; init; }

    public int? OriginalPublicationYear { get; init; }

    public string? OriginalPublisher { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<string> Genres { get; init; } = [];

    public IReadOnlyList<string> Narrators { get; init; } = [];

    public string? AudioPublisher { get; init; }

    public DateOnly? AudiobookReleaseDate { get; init; }

    public string? Language { get; init; }

    public string? Isbn10 { get; init; }

    public string? Isbn13 { get; init; }

    public string? Asin { get; init; }

    public string? EditionName { get; init; }

    public AudiobookAbridgement Abridgement { get; init; } = AudiobookAbridgement.Unknown;

    public string? EditionNotes { get; init; }

    public static AudiobookMetadata FromFileName(string title) =>
        new() { Title = title };
}
