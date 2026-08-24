using Avalonia.Controls;
using Avalonia.Interactivity;
using ListenShelf.Desktop.ViewModels;

namespace ListenShelf.Desktop.Views;

public partial class CustomSleepTimerWindow : Window
{
    public CustomSleepTimerWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            MinutesInput.Focus();
            MinutesInput.SelectAll();
        };
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e) => Close(null);

    private void Start_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CustomSleepTimerViewModel model && model.TryGetMinutes(out var minutes))
            Close(minutes);
    }
}
