using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ListenShelf.Desktop.Views;

public partial class ManagedModePlayerOnlyWarningWindow : Window
{
    public ManagedModePlayerOnlyWarningWindow()
    {
        InitializeComponent();
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e) =>
        Close(false);

    private void OpenTemporarySession_OnClick(object? sender, RoutedEventArgs e) =>
        Close(true);
}
