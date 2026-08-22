using Avalonia.Input;
using ListenShelf.Desktop.Services;

namespace ListenShelf.Tests;

public sealed class LibrarySearchKeyboardShortcutTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CtrlFRequestsSearchWhetherOrNotSearchAlreadyHasFocus(bool searchFocused)
    {
        Assert.Equal(LibrarySearchKeyboardAction.FocusSearch,
            LibrarySearchKeyboardShortcuts.GetAction(Key.F, KeyModifiers.Control, searchFocused));
    }

    [Theory]
    [InlineData(KeyModifiers.None)]
    [InlineData(KeyModifiers.Shift)]
    [InlineData(KeyModifiers.Alt)]
    [InlineData(KeyModifiers.Meta)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Alt)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Meta)]
    public void PlainFAndOtherModifierCombinationsAreNotIntercepted(KeyModifiers modifiers)
    {
        Assert.Null(LibrarySearchKeyboardShortcuts.GetAction(Key.F, modifiers, false));
        Assert.Null(LibrarySearchKeyboardShortcuts.GetAction(Key.F, modifiers, true));
    }

    [Fact]
    public void EscapeOnlyClearsTheFocusedLibrarySearch()
    {
        Assert.Equal(LibrarySearchKeyboardAction.ClearSearch,
            LibrarySearchKeyboardShortcuts.GetAction(Key.Escape, KeyModifiers.None, true));
        Assert.Null(LibrarySearchKeyboardShortcuts.GetAction(Key.Escape, KeyModifiers.None, false));
    }

    [Theory]
    [InlineData(KeyModifiers.Control)]
    [InlineData(KeyModifiers.Shift)]
    [InlineData(KeyModifiers.Alt)]
    [InlineData(KeyModifiers.Meta)]
    public void ModifiedEscapeKeepsItsNormalBehavior(KeyModifiers modifiers)
    {
        Assert.Null(LibrarySearchKeyboardShortcuts.GetAction(Key.Escape, modifiers, true));
    }

    [Theory]
    [InlineData(Key.M)]
    [InlineData(Key.Space)]
    [InlineData(Key.K)]
    [InlineData(Key.Left)]
    [InlineData(Key.Right)]
    [InlineData(Key.Enter)]
    public void SearchShortcutsDoNotConsumePlaybackOrTextEditingKeys(Key key)
    {
        Assert.Null(LibrarySearchKeyboardShortcuts.GetAction(key, KeyModifiers.None, true));
    }
}
