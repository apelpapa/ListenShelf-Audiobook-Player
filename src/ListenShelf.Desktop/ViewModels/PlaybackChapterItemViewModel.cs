namespace ListenShelf.Desktop.ViewModels;

public sealed record PlaybackChapterItemViewModel(
    int Index,
    string Title,
    TimeSpan Start,
    TimeSpan Duration)
{
    public string DisplayText => $"{Index + 1}. {Title}  ·  {FormatTime(Start)}";

    private static string FormatTime(TimeSpan value) => value.TotalHours >= 1d
        ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
        : $"{value.Minutes}:{value.Seconds:00}";
}
