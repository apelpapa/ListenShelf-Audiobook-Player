using Avalonia.Input;

namespace ListenShelf.Desktop.Services;

public enum LibrarySearchKeyboardAction
{
    FocusSearch,
    ClearSearch,
}

public static class LibrarySearchKeyboardShortcuts
{
    public static LibrarySearchKeyboardAction? GetAction(
        Key key, KeyModifiers modifiers, bool isLibrarySearchFocused)
    {
        if (key == Key.F && modifiers == KeyModifiers.Control)
            return LibrarySearchKeyboardAction.FocusSearch;

        if (key == Key.Escape && modifiers == KeyModifiers.None && isLibrarySearchFocused)
            return LibrarySearchKeyboardAction.ClearSearch;

        return null;
    }
}
