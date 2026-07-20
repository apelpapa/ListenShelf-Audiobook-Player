namespace ListenShelf.Application.Library;

public sealed record LibraryBook(
    Guid Id,
    AudiobookMetadata Metadata,
    string FilePath,
    LibraryStorageMode StorageMode,
    long FileSizeBytes,
    DateTimeOffset AddedAtUtc,
    string? CoverPath = null)
{
    public string Title => Metadata.Title;
}
