namespace ListenShelf.Application.Library;

public sealed record BookMetadataSearchResult
{
    public required string SourceName { get; init; }

    public required string SourceId { get; init; }

    public required Uri SourceUri { get; init; }

    public required string Title { get; init; }

    public string? Subtitle { get; init; }

    public IReadOnlyList<string> Authors { get; init; } = [];

    public string? SeriesName { get; init; }

    public string? SeriesPosition { get; init; }

    public int? OriginalPublicationYear { get; init; }

    public string? Publisher { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<string> Genres { get; init; } = [];

    public string? Language { get; init; }

    public string? Isbn10 { get; init; }

    public string? Isbn13 { get; init; }

    public Uri? CoverUri { get; init; }
}
