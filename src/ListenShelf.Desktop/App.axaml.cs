using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ListenShelf.Desktop.Services;
using ListenShelf.Desktop.ViewModels;
using ListenShelf.Desktop.Views;
using ListenShelf.Infrastructure.Library;
using ListenShelf.Infrastructure.Progress;
using ListenShelf.Infrastructure.Settings;
using ListenShelf.Infrastructure.Storage;
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
                var database = new ListenShelfDatabase();
                var themeService = new AvaloniaThemeService();
                var temporaryPlayerSessionService =
                    new AvaloniaTemporaryPlayerSessionService(mainWindow, themeService);
                var viewModel = new MainWindowViewModel(
                    new LibVlcAudioEngine(),
                    new AvaloniaFilePickerService(mainWindow),
                    new SqlitePlaybackProgressStore(database),
                    new SqliteLibrarySettingsStore(database),
                    new SqliteAppSettingsStore(database),
                    themeService,
                    new SqliteAudiobookLibrary(database),
                    new AvaloniaBookMetadataEditorService(mainWindow),
                    temporaryPlayerSessionService);

                mainWindow.DataContext = viewModel;
                mainWindow.Closed += (_, _) => viewModel.Dispose();
                desktop.MainWindow = mainWindow;
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
