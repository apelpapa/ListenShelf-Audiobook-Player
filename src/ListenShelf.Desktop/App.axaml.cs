using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ListenShelf.Desktop.Services;
using ListenShelf.Desktop.ViewModels;
using ListenShelf.Desktop.Views;
using ListenShelf.Infrastructure.Bookmarks;
using ListenShelf.Infrastructure.Library;
using ListenShelf.Infrastructure.Metadata;
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
                var metadataProvider = new OpenLibraryBookMetadataProvider();
                var audiobookLibrary = new SqliteAudiobookLibrary(database);
                var integrityChecker = new SqliteManagedLibraryIntegrityChecker(
                    database,
                    audiobookLibrary.ManagedLibraryPath);
                var viewModel = new MainWindowViewModel(
                    new LibVlcAudioEngine(),
                    new AvaloniaFilePickerService(mainWindow),
                    new SqlitePlaybackProgressStore(database),
                    new SqlitePlaybackBookmarkStore(database),
                    new SqliteAppSettingsStore(database),
                    themeService,
                    audiobookLibrary,
                    new AvaloniaBookMetadataEditorService(mainWindow, metadataProvider),
                    new AvaloniaBookmarkEditorService(mainWindow),
                    new AvaloniaBookRemovalConfirmationService(mainWindow),
                    integrityChecker);

                mainWindow.DataContext = viewModel;
                mainWindow.Opened += async (_, _) =>
                {
                    await viewModel.InitializeAsync();
                    await viewModel.CheckManagedStorageAsync();
                };
                mainWindow.Closed += (_, _) => viewModel.Dispose();
                desktop.MainWindow = mainWindow;
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
