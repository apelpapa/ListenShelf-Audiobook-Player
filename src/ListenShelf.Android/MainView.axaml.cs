using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace ListenShelf.Android;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        SeekBar.AddHandler(PointerPressedEvent, (_, _) => { if (DataContext is MobileViewModel vm) vm.IsSeeking = true; }, RoutingStrategies.Tunnel, true);
        SeekBar.AddHandler(PointerReleasedEvent, (_, _) =>
        {
            if (DataContext is not MobileViewModel vm || !vm.IsSeeking) return;
            var position = SeekBar.Value;
            vm.IsSeeking = false;
            vm.CommitSeek(position);
        }, RoutingStrategies.Bubble, true);
    }
    private void CloseOptions(object? sender, RoutedEventArgs e)
    {
        // Let the option's command run before detaching its data context.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            SpeedButton.Flyout?.Hide();
            SleepButton.Flyout?.Hide();
        });
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this)?.InsetsManager is { } insets)
            insets.SystemBarColor = Color.Parse("#10141F");
        (DataContext as MobileViewModel)?.Attach();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        (DataContext as MobileViewModel)?.Detach();
        base.OnDetachedFromVisualTree(e);
    }
}
