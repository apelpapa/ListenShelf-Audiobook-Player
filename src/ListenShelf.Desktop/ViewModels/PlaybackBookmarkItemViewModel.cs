using CommunityToolkit.Mvvm.Input;
using ListenShelf.Application.Bookmarks;

namespace ListenShelf.Desktop.ViewModels;

public sealed partial class PlaybackBookmarkItemViewModel : ViewModelBase
{
    private readonly Action<PlaybackBookmark> _jumpRequested;
    private readonly Func<PlaybackBookmark, Task> _editRequestedAsync;
    private readonly Action<PlaybackBookmark> _deleteRequested;

    public PlaybackBookmarkItemViewModel(
        PlaybackBookmark bookmark,
        Action<PlaybackBookmark> jumpRequested,
        Func<PlaybackBookmark, Task> editRequestedAsync,
        Action<PlaybackBookmark> deleteRequested)
    {
        Bookmark = bookmark;
        _jumpRequested = jumpRequested;
        _editRequestedAsync = editRequestedAsync;
        _deleteRequested = deleteRequested;
    }

    public PlaybackBookmark Bookmark { get; }

    public string NameText => string.IsNullOrWhiteSpace(Bookmark.Name)
        ? $"Bookmark at {FormatTime(Bookmark.Position)}"
        : Bookmark.Name;

    public string LocationText
    {
        get
        {
            var positionText = FormatTime(Bookmark.Position);
            if (Bookmark.ChapterIndex is not { } chapterIndex)
            {
                return positionText;
            }

            var chapterText = string.IsNullOrWhiteSpace(Bookmark.ChapterTitle)
                ? $"Chapter {chapterIndex + 1}"
                : $"Chapter {chapterIndex + 1}: {Bookmark.ChapterTitle}";
            return $"{chapterText}  •  {positionText}";
        }
    }

    public bool HasNote => !string.IsNullOrWhiteSpace(Bookmark.Note);

    public string NoteText => Bookmark.Note ?? string.Empty;

    [RelayCommand]
    private void Jump() => _jumpRequested(Bookmark);

    [RelayCommand]
    private Task EditAsync() => _editRequestedAsync(Bookmark);

    [RelayCommand]
    private void Delete() => _deleteRequested(Bookmark);

    private static string FormatTime(TimeSpan value) => value.TotalHours >= 1d
        ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
        : $"{value.Minutes}:{value.Seconds:00}";
}
