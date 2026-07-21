namespace ListenShelf.Application.Playback;

public sealed class PlaybackChaptersChangedEventArgs(
    IReadOnlyList<AudioChapter> chapters,
    int currentChapterIndex) : EventArgs
{
    public IReadOnlyList<AudioChapter> Chapters { get; } = chapters;

    public int CurrentChapterIndex { get; } = currentChapterIndex;
}
