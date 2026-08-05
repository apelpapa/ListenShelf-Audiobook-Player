using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ListenShelf.Desktop.Services;
using ListenShelf.Desktop.ViewModels;
using ListenShelf.Desktop.Views;
using ListenShelf.Infrastructure.Backup;
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
                desktop.MainWindow = TryCreateMainWindow(
                    desktop,
                    windowToReplace: null);
            }

            base.OnFrameworkInitializationCompleted();
        }

        private Window TryCreateMainWindow(
            IClassicDesktopStyleApplicationLifetime desktop,
            DatabaseRecoveryWindow? windowToReplace)
        {
            try
            {
                var mainWindow = CreateMainWindow();
                if (windowToReplace is not null)
                {
                    desktop.MainWindow = mainWindow;
                    mainWindow.Show();
                    windowToReplace.Close();
                }

                return mainWindow;
            }
            catch (ListenShelfDatabaseException failure)
            {
                if (windowToReplace?.DataContext is DatabaseRecoveryViewModel existingViewModel)
                {
                    existingViewModel.ApplyFailure(failure);
                    return windowToReplace;
                }

                return CreateRecoveryWindow(desktop, failure);
            }
        }

        private static MainWindow CreateMainWindow()
        {
            var mainWindow = new MainWindow();
            var database = new ListenShelfDatabase();
            var themeService = new AvaloniaThemeService();
            var metadataProvider = new OpenLibraryBookMetadataProvider();
            var audiobookLibrary = new SqliteAudiobookLibrary(database);
            var integrityChecker = new SqliteManagedLibraryIntegrityChecker(
                database,
                audiobookLibrary.ManagedLibraryPath);
            var libraryMaintenance = new SqliteManagedLibraryMaintenance(
                database,
                integrityChecker);
            var backupService = new ZipLibraryBackupService(
                database,
                integrityChecker);
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
                integrityChecker,
                libraryMaintenance,
                backupService);

            mainWindow.DataContext = viewModel;
            mainWindow.Opened += async (_, _) =>
            {
                await viewModel.InitializeAsync();
                await viewModel.CheckManagedStorageAsync();
            };
            mainWindow.Closed += (_, _) => viewModel.Dispose();
            return mainWindow;
        }

        private DatabaseRecoveryWindow CreateRecoveryWindow(
            IClassicDesktopStyleApplicationLifetime desktop,
            ListenShelfDatabaseException failure)
        {
            var recoveryWindow = new DatabaseRecoveryWindow();
            var viewModel = new DatabaseRecoveryViewModel(
                failure,
                new DatabaseRecoveryService(),
                new AvaloniaFilePickerService(recoveryWindow));
            recoveryWindow.DataContext = viewModel;
            viewModel.RecoveryCompleted += (_, _) =>
            {
                _ = TryCreateMainWindow(desktop, recoveryWindow);
            };
            return recoveryWindow;
        }
    }
}
