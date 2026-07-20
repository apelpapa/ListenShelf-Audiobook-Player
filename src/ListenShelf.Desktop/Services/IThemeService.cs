using ListenShelf.Application.Settings;

namespace ListenShelf.Desktop.Services;

public interface IThemeService
{
    void ApplyTheme(AppTheme theme);
}
