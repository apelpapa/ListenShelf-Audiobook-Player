using ListenShelf.Application.Settings;

namespace ListenShelf.Desktop.Services;

public interface ITemporaryPlayerSessionService
{
    Task<bool> WarnAndOpenAsync(AppTheme currentTheme);
}
