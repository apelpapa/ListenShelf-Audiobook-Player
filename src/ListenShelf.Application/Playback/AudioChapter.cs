namespace ListenShelf.Application.Playback;

public sealed record AudioChapter(
    int Index,
    string Title,
    TimeSpan Start,
    TimeSpan Duration);
