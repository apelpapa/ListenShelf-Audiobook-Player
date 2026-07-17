using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ListenShelf.Desktop.Services;
using ListenShelf.Desktop.ViewModels;
using ListenShelf.Desktop.Views;
using ListenShelf.Infrastructure.Progress;
using ListenShelf.Playback.LibVlc;

namespace ListenShelf.Desktop
{
    public partial class App : Avalonia.Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainWindow = new MainWindow();
                var viewModel = new MainWindowViewModel(
                    new LibVlcAudioEngine(),
                    new AvaloniaFilePickerService(mainWindow),
                    new SqlitePlaybackProgressStore());

                mainWindow.DataContext = viewModel;
                mainWindow.Closed += (_, _) => viewModel.Dispose();
                desktop.MainWindow = mainWindow;
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
