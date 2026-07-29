namespace ListenShelf.Application.Bookmarks;

public sealed record PlaybackBookmark(
    Guid Id,
    string FilePath,
    TimeSpan Position,
    string? Name,
    string? Note,
    int? ChapterIndex,
    string? ChapterTitle,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
