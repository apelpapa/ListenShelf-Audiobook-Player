using CommunityToolkit.Mvvm.Input;

namespace ListenShelf.Desktop.ViewModels;

public sealed partial class ChapterSearchResultViewModel(
    PlaybackChapterItemViewModel chapter,
    Action<PlaybackChapterItemViewModel> navigate) : ViewModelBase
{
    public PlaybackChapterItemViewModel Chapter { get; } = chapter;

    public string DisplayText => Chapter.DisplayText;

    [RelayCommand]
    private void GoTo() => navigate(Chapter);
}
