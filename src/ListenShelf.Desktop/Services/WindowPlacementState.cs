using Avalonia.Controls;
using ListenShelf.Application.Settings;

namespace ListenShelf.Desktop.Services;

public sealed class WindowPlacementState(WindowPlacement initial)
{
    public WindowPlacement Current { get; private set; } = initial;

    public void ObserveState(WindowState state)
    {
        if (state is WindowState.Normal or WindowState.Maximized)
            Current = Current with { IsMaximized = state == WindowState.Maximized };
        // Never reopen minimized or fullscreen. Keep the last normal/maximized choice.
    }

    public void Observe(WindowState state, WindowPlacement bounds)
    {
        ObserveState(state);
        if (state == WindowState.Normal && bounds.IsValid())
            Current = bounds with { IsMaximized = false };
    }

    public void SetSafeBounds(WindowPlacement bounds) => Current = bounds;
}
