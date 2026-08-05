using Avalonia.Controls;
using ListenShelf.Application.Playback;

namespace ListenShelf.Desktop.Services;

public sealed class NoOpGlobalMediaKeyService : IGlobalMediaKeyService
{
    public void Attach(
        Window window,
        Func<PlaybackControlAction, bool> controlRequested)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(controlRequested);
    }

    public void SetEnabled(bool isEnabled)
    {
    }

    public void Dispose()
    {
    }
}
