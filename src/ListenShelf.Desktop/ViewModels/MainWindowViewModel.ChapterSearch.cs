using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ListenShelf.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChapterSearchText))]
    private string _chapterSearchText = string.Empty;

    [ObservableProperty]
    private bool _isChapterSearchExpanded;

    public ObservableCollection<ChapterSearchResultViewModel> ChapterSearchResults { get; } = [];

    public bool HasChapterSearchText => !string.IsNullOrWhiteSpace(ChapterSearchText);

    public bool CanUseChapterSearch => !_disposed && CanControlPlayback && HasChapters;

    public bool HasChapterSearchResults => ChapterSearchResults.Count > 0;

    public bool HasNoChapterSearchResults => !HasChapterSearchResults;

    public string ChapterSearchCountText => HasChapterSearchText
        ? $"{ChapterSearchResults.Count} of {Chapters.Count} {(Chapters.Count == 1 ? "chapter" : "chapters")}"
        : Chapters.Count == 1 ? "1 chapter" : $"{Chapters.Count} chapters";

    [RelayCommand]
    private void ClearChapterSearch() => ChapterSearchText = string.Empty;

    partial void OnChapterSearchTextChanged(string value) => ApplyChapterSearch();

    private void OnChapterSearchCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Native chapter lists are replaced in a batch. Refilter once at its end,
        // instead of rebuilding results after every chapter in a long audiobook.
        if (!_isUpdatingChapterFromEngine) ApplyChapterSearch();
    }

    private void ApplyChapterSearch()
    {
        var query = ChapterSearchText?.Trim() ?? string.Empty;
        ChapterSearchResults.Clear();
        foreach (var chapter in Chapters)
        {
            var number = (chapter.Index + 1).ToString(CultureInfo.InvariantCulture);
            if (query.Length == 0
                || chapter.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || number.Contains(query, StringComparison.Ordinal))
            {
                ChapterSearchResults.Add(new ChapterSearchResultViewModel(chapter, NavigateToChapterSearchResult));
            }
        }

        OnPropertyChanged(nameof(HasChapters));
        OnPropertyChanged(nameof(CanUseChapterSearch));
        OnPropertyChanged(nameof(HasChapterSearchResults));
        OnPropertyChanged(nameof(HasNoChapterSearchResults));
        OnPropertyChanged(nameof(ChapterSearchCountText));
    }

    private void NavigateToChapterSearchResult(PlaybackChapterItemViewModel chapter)
    {
        // A queued result from another book or an old metadata snapshot must not navigate.
        if (!CanUseChapterSearch || !Chapters.Any(current => ReferenceEquals(current, chapter))) return;

        try
        {
            ErrorMessage = string.Empty;
            SelectChapter(chapter.Index);
        }
        catch (Exception exception)
        {
            ErrorMessage = $"That chapter could not be opened: {exception.Message}";
        }
    }

    private void ResetChapterSearch()
    {
        ChapterSearchText = string.Empty;
        IsChapterSearchExpanded = false;
        ApplyChapterSearch();
    }
}
