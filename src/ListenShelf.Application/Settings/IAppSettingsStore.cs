namespace ListenShelf.Application.Settings;

public interface IAppSettingsStore
{
    WindowPlacement? GetWindowPlacement();

    void SaveWindowPlacement(WindowPlacement placement);

    AppTheme GetTheme();

    void SaveTheme(AppTheme theme);

    LibraryViewMode GetLibraryViewMode();

    void SaveLibraryViewMode(LibraryViewMode viewMode);

    LibraryGroupMode GetLibraryGroupMode();

    void SaveLibraryGroupMode(LibraryGroupMode groupMode);

    LibrarySortMode GetLibrarySortMode();

    void SaveLibrarySortMode(LibrarySortMode sortMode);

    bool GetLibrarySortReversed();

    void SaveLibrarySortReversed(bool reversed);

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

    int? GetLastSleepTimerMinutes();

    void SaveLastSleepTimerMinutes(int minutes);
}
