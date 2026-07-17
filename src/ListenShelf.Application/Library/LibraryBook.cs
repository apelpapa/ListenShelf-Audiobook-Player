namespace ListenShelf.Application.Library;

public sealed record LibraryBook(
    Guid Id,
    string Title,
    string FilePath,
    LibraryStorageMode StorageMode,
    long FileSizeBytes,
    DateTimeOffset AddedAtUtc,
    string? CoverPath = null);
