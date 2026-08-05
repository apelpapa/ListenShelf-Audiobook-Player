using Avalonia.Controls;
using ListenShelf.Application.Playback;

namespace ListenShelf.Desktop.Services;

public interface IGlobalMediaKeyService : IDisposable
{
    void Attach(
        Window window,
        Func<PlaybackControlAction, bool> controlRequested);

    void SetEnabled(bool isEnabled);
}
