namespace ListenShelf.Application.Settings;

public interface IAppSettingsStore
{
    AppTheme GetTheme();

    void SaveTheme(AppTheme theme);

    LibraryViewMode GetLibraryViewMode();

    void SaveLibraryViewMode(LibraryViewMode viewMode);
}
