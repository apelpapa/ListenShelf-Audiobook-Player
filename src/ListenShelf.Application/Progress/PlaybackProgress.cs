namespace ListenShelf.Application.Progress;

public sealed record PlaybackProgress(
    string FilePath,
    TimeSpan Position,
    TimeSpan Duration,
    DateTimeOffset UpdatedAtUtc);
