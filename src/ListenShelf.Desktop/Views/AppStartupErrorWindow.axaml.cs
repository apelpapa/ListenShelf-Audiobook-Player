using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ListenShelf.Desktop.Views;

public partial class AppStartupErrorWindow : Window
{
    public AppStartupErrorWindow()
    {
        InitializeComponent();
    }

    private void Exit_OnClick(object? sender, RoutedEventArgs e) => Close();
}
