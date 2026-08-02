using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ListenShelf.Desktop.Views;

public partial class BookRemovalConfirmationWindow : Window
{
    public BookRemovalConfirmationWindow()
    {
        InitializeComponent();
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e) =>
        Close(false);

    private void Remove_OnClick(object? sender, RoutedEventArgs e) =>
        Close(true);
}
