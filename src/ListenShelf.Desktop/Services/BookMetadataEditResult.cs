using ListenShelf.Application.Library;

namespace ListenShelf.Desktop.Services;

public sealed record BookMetadataEditResult(
    AudiobookMetadata Metadata,
    BookCoverImage? CoverImage);
