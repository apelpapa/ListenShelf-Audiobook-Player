using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace ListenShelf.Android;

public partial class App : Avalonia.Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IActivityApplicationLifetime lifetime)
            lifetime.MainViewFactory = () => MobileSession.StartupFailure is { } error
                ? new ScrollViewer { Content = new TextBlock {
                    Text = "ListenShelf couldn't start.\n\n" + error.GetBaseException().Message + "\n\nClose and reopen the app to retry.",
                    Margin = new Thickness(26,60), TextWrapping = Avalonia.Media.TextWrapping.Wrap } }
                : new MainView { DataContext = new MobileViewModel(MobileSession.Instance) };
        base.OnFrameworkInitializationCompleted();
    }
}
