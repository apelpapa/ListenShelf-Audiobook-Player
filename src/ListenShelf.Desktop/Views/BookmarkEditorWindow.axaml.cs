using Avalonia.Controls;
using Avalonia.Interactivity;
using ListenShelf.Desktop.ViewModels;

namespace ListenShelf.Desktop.Views;

public partial class BookmarkEditorWindow : Window
{
    public BookmarkEditorWindow()
    {
        InitializeComponent();
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e) =>
        Close(null);

    private void Save_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BookmarkEditorViewModel viewModel)
        {
            Close(viewModel.CreateResult());
        }
    }
}
