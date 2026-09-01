using CommunityToolkit.Mvvm.ComponentModel;
using ListenShelf.Application.Settings;

namespace ListenShelf.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LibrarySortDirectionToolTip))]
    private bool _isLibrarySortReversed;

    public string LibrarySortDirectionToolTip
    {
        get
        {
            var defaultOrder = SelectedLibrarySortOption.Mode switch
            {
                LibrarySortMode.Author => "author A–Z",
                LibrarySortMode.SeriesOrder => "series A–Z, then lowest book number first",
                LibrarySortMode.RecentlyPlayed => "most recently played first",
                LibrarySortMode.DateAdded => "newest additions first",
                LibrarySortMode.Progress => "highest listening percentage first",
                _ => "title A–Z",
            };

            return IsLibrarySortReversed
                ? $"Reverse order is on. Click to restore {defaultOrder}. This choice is remembered."
                : $"Current order: {defaultOrder}. Click to reverse the whole order, last to first. This choice is remembered.";
        }
    }

    partial void OnIsLibrarySortReversedChanged(bool value)
    {
        ApplyLibraryQuery();
        try
        {
            _appSettingsStore.SaveLibrarySortReversed(value);
        }
        catch (Exception exception)
        {
            LibraryStatusMessage = $"Sort direction changed for this session, but could not be saved: {exception.Message}";
        }
    }
}
