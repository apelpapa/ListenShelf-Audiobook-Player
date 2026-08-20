using Avalonia.Controls;
using Avalonia.Interactivity;
using ListenShelf.Desktop.ViewModels;

namespace ListenShelf.Desktop.Views;

public partial class JumpToTimeWindow : Window
{
    public JumpToTimeWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            TimeInput.Focus();
            TimeInput.SelectAll();
        };
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e) => Close(null);

    private void Jump_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is JumpToTimeViewModel model && model.TryGetPosition(out var position))
        {
            Close(position);
        }
    }
}
