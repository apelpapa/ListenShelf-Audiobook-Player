using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ListenShelf.Application.Settings;

namespace ListenShelf.Desktop.Services;

public sealed class WindowPlacementController : IDisposable
{
    private readonly Window _window;
    private readonly IAppSettingsStore _settings;
    private readonly Action<string, Exception> _logError;
    private readonly WindowPlacementState _state;
    private bool _opened;
    private bool _disposed;
    private bool _captureQueued;
    private bool _screensChanged;
    private bool _maximizeOnOpen;

    public WindowPlacementController(Window window, IAppSettingsStore settings, Action<string, Exception> logError)
    {
        _window = window;
        _settings = settings;
        _logError = logError;
        WindowPlacement? saved = null;
        try { saved = settings.GetWindowPlacement(); }
        catch (Exception exception) { _logError("Window layout could not be loaded; using a safe default.", exception); }
        _state = new WindowPlacementState(saved ?? new WindowPlacement(0, 0,
            WindowPlacementGeometry.DefaultWidth, WindowPlacementGeometry.DefaultHeight, false));
        try
        {
            if (Resolve(saved) is { } restored)
            {
                ApplyBounds(restored);
                _state.SetSafeBounds(restored.Placement);
                // Establish native normal bounds before maximizing, so Restore
                // Down has the saved size even on a launch that starts maximized.
                _maximizeOnOpen = restored.Placement.IsMaximized;
                _window.WindowState = WindowState.Normal;
            }
            else _window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        catch (Exception exception) { _logError("Window layout could not be restored.", exception); }

        window.Opened += OnOpened;
        window.PositionChanged += OnPositionChanged;
        window.Resized += OnResized;
        window.PropertyChanged += OnPropertyChanged;
        window.Screens.Changed += OnScreensChanged;
    }

    public void Save()
    {
        if (_disposed || !_opened) return;
        try
        {
            Capture();
            _settings.SaveWindowPlacement(_state.Current);
        }
        catch (Exception exception) { _logError("Window layout could not be saved; closing will continue.", exception); }
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        _opened = true;
        if (_maximizeOnOpen) _window.WindowState = WindowState.Maximized;
        QueueCapture();
    }
    private void OnPositionChanged(object? sender, PixelPointEventArgs e) => QueueCapture();
    private void OnResized(object? sender, WindowResizedEventArgs e) => QueueCapture();
    private void OnScreensChanged(object? sender, EventArgs e) { _screensChanged = true; QueueCapture(); }

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Window.WindowStateProperty || !_opened || _disposed) return;
        _state.ObserveState(_window.WindowState);
        QueueCapture();
    }

    private void QueueCapture()
    {
        if (!_opened || _disposed || _captureQueued) return;
        _captureQueued = true;
        // Win32 can send a resize before the corresponding maximize/state event.
        // Sample once that notification burst has settled, never its temporary bounds.
        Dispatcher.UIThread.Post(() =>
        {
            _captureQueued = false;
            if (_disposed) return;
            try
            {
                Capture();
                if (_screensChanged)
                {
                    _screensChanged = false;
                    if (Resolve(_state.Current) is { } safe)
                    {
                        _state.SetSafeBounds(safe.Placement);
                        if (_window.WindowState == WindowState.Normal) ApplyBounds(safe);
                    }
                }
            }
            catch (Exception exception) { _logError("Window layout could not be tracked.", exception); }
        }, DispatcherPriority.Loaded);
    }

    private void Capture()
    {
        var position = _window.Position;
        var size = _window.ClientSize;
        _state.Observe(_window.WindowState, new WindowPlacement(position.X, position.Y, size.Width, size.Height, false));
        if (_window.WindowState == WindowState.Maximized && _window.Screens.ScreenFromWindow(_window) is { } screen)
        {
            // Win+Shift+Arrow can move a maximized window without a normal-state
            // event. Keep its next-launch restore bounds on that current monitor.
            var safe = WindowPlacementGeometry.Resolve(_state.Current,
                [new WindowPlacementScreen(screen.WorkingArea, screen.Scaling, screen.IsPrimary)]);
            if (safe is not null) _state.SetSafeBounds(safe.Placement);
        }
    }

    private WindowPlacementResult? Resolve(WindowPlacement? saved)
    {
        var frame = _window.FrameSize;
        var client = _window.ClientSize;
        var frameWidth = frame is { } outer && outer.Width > client.Width ? outer.Width - client.Width : 16;
        var frameHeight = frame is { } outerHeight && outerHeight.Height > client.Height ? outerHeight.Height - client.Height : 40;
        return WindowPlacementGeometry.Resolve(saved, _window.Screens.All.Select(screen =>
            new WindowPlacementScreen(screen.WorkingArea, screen.Scaling, screen.IsPrimary)), frameWidth, frameHeight);
    }

    private void ApplyBounds(WindowPlacementResult result)
    {
        _window.MinWidth = result.MinimumWidth;
        _window.MinHeight = result.MinimumHeight;
        _window.WindowStartupLocation = WindowStartupLocation.Manual;
        // Move first so the target monitor's DPI can settle before applying DIP sizes.
        _window.Position = new PixelPoint(result.Placement.X, result.Placement.Y);
        _window.Width = result.Placement.Width;
        _window.Height = result.Placement.Height;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _window.Opened -= OnOpened;
        _window.PositionChanged -= OnPositionChanged;
        _window.Resized -= OnResized;
        _window.PropertyChanged -= OnPropertyChanged;
        _window.Screens.Changed -= OnScreensChanged;
    }
}
