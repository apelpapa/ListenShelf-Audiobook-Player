using System.Collections.Specialized;

namespace ListenShelf.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    public bool HasChapterListeningTimeEstimate => GetChapterListeningSeconds() is not null;

    public string ChapterListeningTimeRemainingText => GetChapterListeningSeconds() is { } seconds
        ? $"Chapter time left: ~{FormatTime(seconds, seconds)} at {SelectedPlaybackRate:0.##}×"
        : string.Empty;

    private double? GetChapterListeningSeconds()
    {
        if (!IsFileLoaded || Chapters.Count == 0 || !double.IsFinite(PositionSeconds)
            || PositionSeconds < 0 || !PlaybackRates.Contains(SelectedPlaybackRate))
        {
            return null;
        }

        // Use the playhead, not a potentially delayed engine selection event,
        // so seeking into another chapter updates the countdown immediately.
        var current = -1;
        var previousStart = -1d;
        for (var index = 0; index < Chapters.Count; index++)
        {
            var start = Chapters[index].Start.TotalSeconds;
            // Duplicate/default-zero or out-of-order offsets cannot identify
            // the current chapter reliably. Don't display a guessed countdown.
            if (start < 0 || start <= previousStart) return null;
            previousStart = start;
            if (start <= PositionSeconds) current = index;
        }

        if (current < 0) return null;
        var chapter = Chapters[current];
        var chapterStart = chapter.Start.TotalSeconds;
        var nextStart = current + 1 < Chapters.Count ? Chapters[current + 1].Start.TotalSeconds : double.NaN;
        var bookEnd = double.IsFinite(DurationSeconds) && DurationSeconds > 0 ? DurationSeconds : double.NaN;
        var end = chapter.Duration > TimeSpan.Zero ? chapterStart + chapter.Duration.TotalSeconds : nextStart;
        if (!double.IsFinite(end)) end = bookEnd;
        if (!double.IsFinite(end)) return null;
        if (double.IsFinite(nextStart)) end = Math.Min(end, nextStart);
        if (double.IsFinite(bookEnd)) end = Math.Min(end, bookEnd);
        if (end <= chapterStart) return null;

        // A gap after a chapter is not listening time in that chapter. The last
        // chapter may still show zero at/past the known end of the audiobook.
        if (PositionSeconds > end
            && !(current == Chapters.Count - 1 && end == bookEnd && PositionSeconds >= bookEnd))
        {
            return null;
        }

        var remaining = Math.Ceiling(Math.Max(0d, end - PositionSeconds) / SelectedPlaybackRate);
        return double.IsFinite(remaining) && remaining < TimeSpan.MaxValue.TotalSeconds ? remaining : null;
    }

    private void OnChapterTimingCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasChapterListeningTimeEstimate));
        OnPropertyChanged(nameof(ChapterListeningTimeRemainingText));
    }
}
