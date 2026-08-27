using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ListenShelf.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBookmarkSearchText))]
    private string _bookmarkSearchText = string.Empty;

    public ObservableCollection<PlaybackBookmarkItemViewModel> FilteredBookmarks { get; } = [];

    public bool HasBookmarkSearchText => !string.IsNullOrWhiteSpace(BookmarkSearchText);

    public bool HasMatchingBookmarks => FilteredBookmarks.Count > 0;

    public bool HasNoMatchingBookmarks => HasBookmarks && !HasMatchingBookmarks;

    [RelayCommand]
    private void ClearBookmarkSearch() => BookmarkSearchText = string.Empty;

    partial void OnBookmarkSearchTextChanged(string value) => ApplyBookmarkSearch();

    private void ApplyBookmarkSearch()
    {
        var query = BookmarkSearchText?.Trim() ?? string.Empty;
        FilteredBookmarks.Clear();
        foreach (var item in Bookmarks)
        {
            // Search the displayed fallback name too, so unnamed quick bookmarks are findable.
            // Keep the store's timestamp order and the existing item commands intact.
            if (query.Length == 0
                || item.NameText.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.NoteText.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                FilteredBookmarks.Add(item);
            }
        }

        OnPropertyChanged(nameof(HasMatchingBookmarks));
        OnPropertyChanged(nameof(HasNoMatchingBookmarks));
        OnPropertyChanged(nameof(BookmarkCountText));
    }
}
