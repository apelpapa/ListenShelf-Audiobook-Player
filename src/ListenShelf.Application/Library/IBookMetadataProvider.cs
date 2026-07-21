namespace ListenShelf.Application.Library;

public interface IBookMetadataProvider
{
    string Name { get; }

    Task<IReadOnlyList<BookMetadataSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default);

    Task<BookCoverImage?> DownloadCoverAsync(
        Uri coverUri,
        CancellationToken cancellationToken = default);
}
