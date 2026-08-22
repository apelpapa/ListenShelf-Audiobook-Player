using Avalonia.Controls;

namespace ListenShelf.Desktop.Views.Sections;

public partial class LibraryView : UserControl
{
    public LibraryView()
    {
        InitializeComponent();
    }

    public bool IsSearchFocused => LibrarySearchBox.IsFocused;

    public void FocusSearch()
    {
        if (LibrarySearchBox.IsEffectivelyVisible && LibrarySearchBox.IsEffectivelyEnabled
            && (LibrarySearchBox.IsFocused || LibrarySearchBox.Focus()))
        {
            LibrarySearchBox.SelectAll();
        }
    }
}
