namespace ListenShelf.Application.Settings;

public interface IAppSettingsStore
{
    AppTheme GetTheme();

    void SaveTheme(AppTheme theme);

    LibraryViewMode GetLibraryViewMode();

    void SaveLibraryViewMode(LibraryViewMode viewMode);

    LibraryGroupMode GetLibraryGroupMode();

    void SaveLibraryGroupMode(LibraryGroupMode groupMode);

    double GetLibraryTileWidth();

    void SaveLibraryTileWidth(double tileWidth);
}
