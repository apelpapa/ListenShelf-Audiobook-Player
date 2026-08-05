using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ListenShelf.Desktop.Views;

public partial class DatabaseRecoveryWindow : Window
{
    public DatabaseRecoveryWindow()
    {
        InitializeComponent();
    }

    private void Exit_OnClick(object? sender, RoutedEventArgs e) => Close();
}
