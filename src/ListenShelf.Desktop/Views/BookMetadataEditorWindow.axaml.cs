using Avalonia.Controls;
using Avalonia.Interactivity;
using ListenShelf.Application.Library;
using ListenShelf.Desktop.ViewModels;

namespace ListenShelf.Desktop.Views;

public partial class BookMetadataEditorWindow : Window
{
    public BookMetadataEditorWindow()
    {
        InitializeComponent();
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e) =>
        Close(null);

    private void Save_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BookMetadataEditorViewModel viewModel
            && viewModel.TryCreateMetadata(out var metadata))
        {
            Close(metadata);
        }
    }
}
