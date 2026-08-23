using Avalonia.Input;
using ListenShelf.Application.Playback;
using ListenShelf.Desktop.Services;

namespace ListenShelf.Tests;

public sealed class KeyboardShortcutReferenceTests
{
    [Theory]
    [InlineData("Space / K", "Play or pause", Key.Space, PlaybackControlAction.TogglePlayPause)]
    [InlineData("Space / K", "Play or pause", Key.K, PlaybackControlAction.TogglePlayPause)]
    [InlineData("Left Arrow / J", "Rewind", Key.Left, PlaybackControlAction.SkipBackward)]
    [InlineData("Left Arrow / J", "Rewind", Key.J, PlaybackControlAction.SkipBackward)]
    [InlineData("Right Arrow / L", "Skip forward", Key.Right, PlaybackControlAction.SkipForward)]
    [InlineData("Right Arrow / L", "Skip forward", Key.L, PlaybackControlAction.SkipForward)]
    [InlineData("M", "Mute or unmute", Key.M, PlaybackControlAction.ToggleMute)]
    public void DisplayedPlaybackShortcutsMatchTheActualRouter(
        string keys, string description, Key key, PlaybackControlAction action)
    {
        var entry = Assert.Single(KeyboardShortcutReference.MainWindow, entry => entry.Keys == keys);
        Assert.Equal(description, entry.Description);
        Assert.Equal(action, PlaybackKeyboardShortcuts.GetAction(key, KeyModifiers.None, PlaybackKeyboardFocus.Other));
        Assert.Null(PlaybackKeyboardShortcuts.GetAction(key, KeyModifiers.None, PlaybackKeyboardFocus.TextInput));
        Assert.Null(PlaybackKeyboardShortcuts.GetAction(key, KeyModifiers.None, PlaybackKeyboardFocus.ComboBox));
    }

    [Theory]
    [InlineData("Ctrl+F", "Search the library", Key.F, KeyModifiers.Control, false, LibrarySearchKeyboardAction.FocusSearch)]
    [InlineData("Ctrl+F", "Search the library", Key.F, KeyModifiers.Control, true, LibrarySearchKeyboardAction.FocusSearch)]
    [InlineData("Escape", "Clear library search", Key.Escape, KeyModifiers.None, true, LibrarySearchKeyboardAction.ClearSearch)]
    public void DisplayedSearchShortcutsMatchTheActualRouter(
        string keys, string description, Key key, KeyModifiers modifiers, bool focused, LibrarySearchKeyboardAction action)
    {
        var entry = Assert.Single(KeyboardShortcutReference.MainWindow, entry => entry.Keys == keys);
        Assert.Equal(description, entry.Description);
        Assert.Equal(action, LibrarySearchKeyboardShortcuts.GetAction(key, modifiers, focused));
    }

    [Fact]
    public void ReferenceKeepsContextAndDialogKeysSeparate_WithoutHardCodedSkipDurations()
    {
        Assert.Equal(6, KeyboardShortcutReference.MainWindow.Count);
        Assert.Equal(2, KeyboardShortcutReference.JumpToTimeDialog.Count);
        foreach (var entries in new[] { KeyboardShortcutReference.MainWindow, KeyboardShortcutReference.JumpToTimeDialog })
        {
            Assert.Equal(entries.Count, entries.Select(entry => entry.Keys).Distinct().Count());
            Assert.All(entries, entry =>
            {
                Assert.False(string.IsNullOrWhiteSpace(entry.Keys));
                Assert.False(string.IsNullOrWhiteSpace(entry.Description));
                Assert.False(string.IsNullOrWhiteSpace(entry.Context));
            });
        }
        foreach (var keys in new[] { "Left Arrow / J", "Right Arrow / L" })
        {
            var entry = Assert.Single(KeyboardShortcutReference.MainWindow, entry => entry.Keys == keys);
            Assert.Contains("interval", entry.Context);
            Assert.DoesNotContain(entry.Context, char.IsDigit);
        }
        Assert.Contains("search box is focused", KeyboardShortcutReference.MainWindow.Single(entry => entry.Keys == "Escape").Context);
        Assert.Contains("without seeking", KeyboardShortcutReference.JumpToTimeDialog.Single(entry => entry.Keys == "Escape").Context);
        Assert.Contains("valid", KeyboardShortcutReference.JumpToTimeDialog.Single(entry => entry.Keys == "Enter").Context);
        Assert.Null(LibrarySearchKeyboardShortcuts.GetAction(Key.Escape, KeyModifiers.None, false));
    }
}
