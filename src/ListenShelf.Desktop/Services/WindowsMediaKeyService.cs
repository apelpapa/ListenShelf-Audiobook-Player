using System.Runtime.InteropServices;
using Avalonia.Controls;
using ListenShelf.Application.Playback;

namespace ListenShelf.Desktop.Services;

public sealed class WindowsMediaKeyService : IGlobalMediaKeyService
{
    private const uint WmHotKey = 0x0312;
    private const uint WmAppCommand = 0x0319;
    private const uint ModNoRepeat = 0x4000;

    private const int PlayPauseHotKeyId = 0x4C51;
    private const int PreviousHotKeyId = 0x4C52;
    private const int NextHotKeyId = 0x4C53;
    private const int StopHotKeyId = 0x4C54;

    private const uint VkMediaNextTrack = 0xB0;
    private const uint VkMediaPreviousTrack = 0xB1;
    private const uint VkMediaStop = 0xB2;
    private const uint VkMediaPlayPause = 0xB3;

    private readonly HashSet<int> _registeredHotKeys = [];
    private Window? _window;
    private nint _windowHandle;
    private Func<PlaybackControlAction, bool>? _controlRequested;
    private Win32Properties.CustomWndProcHookCallback? _wndProcHook;
    private bool _isEnabled;
    private bool _disposed;

    public void Attach(
        Window window,
        Func<PlaybackControlAction, bool> controlRequested)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!OperatingSystem.IsWindows() || _window is not null)
        {
            return;
        }

        var platformHandle = window.TryGetPlatformHandle();
        if (platformHandle is null)
        {
            return;
        }

        _window = window;
        _windowHandle = platformHandle.Handle;
        _controlRequested = controlRequested;
        _wndProcHook = HandleWindowMessage;
        Win32Properties.AddWndProcHookCallback(window, _wndProcHook);
    }

    public void SetEnabled(bool isEnabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_window is null || _isEnabled == isEnabled)
        {
            return;
        }

        _isEnabled = isEnabled;
        if (_isEnabled)
        {
            RegisterMediaHotKeys();
        }
        else
        {
            UnregisterMediaHotKeys();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        UnregisterMediaHotKeys();

        if (_window is not null && _wndProcHook is not null)
        {
            Win32Properties.RemoveWndProcHookCallback(_window, _wndProcHook);
        }

        _controlRequested = null;
        _wndProcHook = null;
        _window = null;
        _windowHandle = nint.Zero;
        _disposed = true;
    }

    private nint HandleWindowMessage(
        nint hWnd,
        uint message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (!_isEnabled)
        {
            return nint.Zero;
        }

        PlaybackControlAction? action = message switch
        {
            WmHotKey => GetHotKeyAction(wParam),
            WmAppCommand => GetAppCommandAction(lParam),
            _ => null,
        };

        if (action is not null
            && _controlRequested?.Invoke(action.Value) == true)
        {
            handled = true;
        }

        return nint.Zero;
    }

    private void RegisterMediaHotKeys()
    {
        RegisterMediaHotKey(PlayPauseHotKeyId, VkMediaPlayPause);
        RegisterMediaHotKey(PreviousHotKeyId, VkMediaPreviousTrack);
        RegisterMediaHotKey(NextHotKeyId, VkMediaNextTrack);
        RegisterMediaHotKey(StopHotKeyId, VkMediaStop);
    }

    private void RegisterMediaHotKey(int id, uint virtualKey)
    {
        if (RegisterHotKey(_windowHandle, id, ModNoRepeat, virtualKey))
        {
            _registeredHotKeys.Add(id);
        }
    }

    private void UnregisterMediaHotKeys()
    {
        foreach (var id in _registeredHotKeys)
        {
            UnregisterHotKey(_windowHandle, id);
        }

        _registeredHotKeys.Clear();
        _isEnabled = false;
    }

    private static PlaybackControlAction? GetHotKeyAction(nint hotKeyId) =>
        hotKeyId.ToInt32() switch
        {
            PlayPauseHotKeyId => PlaybackControlAction.TogglePlayPause,
            PreviousHotKeyId => PlaybackControlAction.SkipBackward,
            NextHotKeyId => PlaybackControlAction.SkipForward,
            StopHotKeyId => PlaybackControlAction.Pause,
            _ => null,
        };

    private static PlaybackControlAction? GetAppCommandAction(nint lParam)
    {
        const int appCommandMediaNextTrack = 11;
        const int appCommandMediaPreviousTrack = 12;
        const int appCommandMediaStop = 13;
        const int appCommandMediaPlayPause = 14;
        const int appCommandMediaPlay = 46;
        const int appCommandMediaPause = 47;
        const int appCommandMediaFastForward = 49;
        const int appCommandMediaRewind = 50;

        var command = (int)((lParam.ToInt64() >> 16) & 0x0FFF);
        return command switch
        {
            appCommandMediaNextTrack or appCommandMediaFastForward =>
                PlaybackControlAction.SkipForward,
            appCommandMediaPreviousTrack or appCommandMediaRewind =>
                PlaybackControlAction.SkipBackward,
            appCommandMediaStop or appCommandMediaPause =>
                PlaybackControlAction.Pause,
            appCommandMediaPlayPause =>
                PlaybackControlAction.TogglePlayPause,
            appCommandMediaPlay =>
                PlaybackControlAction.Play,
            _ => null,
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(
        nint hWnd,
        int id,
        uint modifiers,
        uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);
}
