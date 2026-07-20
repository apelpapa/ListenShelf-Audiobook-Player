using Avalonia.Styling;
using ListenShelf.Application.Settings;

namespace ListenShelf.Desktop.Services;

public sealed class AvaloniaThemeService : IThemeService
{
    public void ApplyTheme(AppTheme theme)
    {
        var application = Avalonia.Application.Current
            ?? throw new InvalidOperationException("The application theme is not available yet.");

        application.RequestedThemeVariant = theme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            _ => ThemeVariant.Dark,
        };
    }
}
