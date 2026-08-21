using Avalonia.Input;
using ListenShelf.Application.Playback;

namespace ListenShelf.Desktop.Services;

public enum PlaybackKeyboardFocus
{
    Other,
    TextInput,
    Button,
    ComboBox,
    Slider,
}

public static class PlaybackKeyboardShortcuts
{
    public static PlaybackControlAction? GetAction(
        Key key, KeyModifiers modifiers, PlaybackKeyboardFocus focus)
    {
        if (modifiers != KeyModifiers.None || focus == PlaybackKeyboardFocus.TextInput) return null;

        return key switch
        {
            Key.Space when focus is not (PlaybackKeyboardFocus.Button or PlaybackKeyboardFocus.ComboBox) =>
                PlaybackControlAction.TogglePlayPause,
            Key.K when focus != PlaybackKeyboardFocus.ComboBox => PlaybackControlAction.TogglePlayPause,
            Key.Left when focus is not (PlaybackKeyboardFocus.Slider or PlaybackKeyboardFocus.ComboBox) =>
                PlaybackControlAction.SkipBackward,
            Key.J when focus != PlaybackKeyboardFocus.ComboBox => PlaybackControlAction.SkipBackward,
            Key.Right when focus is not (PlaybackKeyboardFocus.Slider or PlaybackKeyboardFocus.ComboBox) =>
                PlaybackControlAction.SkipForward,
            Key.L when focus != PlaybackKeyboardFocus.ComboBox => PlaybackControlAction.SkipForward,
            Key.M when focus != PlaybackKeyboardFocus.ComboBox => PlaybackControlAction.ToggleMute,
            _ => null,
        };
    }
}
