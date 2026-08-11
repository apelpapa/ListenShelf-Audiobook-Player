namespace ListenShelf.Application.Settings;

public interface IAppSettingsStore
{
    AppTheme GetTheme();

    void SaveTheme(AppTheme theme);

    LibraryViewMode GetLibraryViewMode();

    void SaveLibraryViewMode(LibraryViewMode viewMode);

    LibraryGroupMode GetLibraryGroupMode();

    void SaveLibraryGroupMode(LibraryGroupMode groupMode);

    LibrarySortMode GetLibrarySortMode();

    void SaveLibrarySortMode(LibrarySortMode sortMode);

    LibraryStatusFilter GetLibraryStatusFilter();

    void SaveLibraryStatusFilter(LibraryStatusFilter statusFilter);

    double GetLibraryTileWidth();

    void SaveLibraryTileWidth(double tileWidth);

    double GetPlaybackVolume();

    void SavePlaybackVolume(double volume);

    double GetPlaybackRate();

    void SavePlaybackRate(double rate);

    int GetRewindSeconds();

    void SaveRewindSeconds(int seconds);

    int GetForwardSeconds();

    void SaveForwardSeconds(int seconds);
}
