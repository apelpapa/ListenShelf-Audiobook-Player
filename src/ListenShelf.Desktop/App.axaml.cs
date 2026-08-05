using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ListenShelf.Desktop.Diagnostics;
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
        private readonly AppDiagnosticLog _diagnosticLog =
            AppDiagnosticLog.CreateDefault();

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                _diagnosticLog.WriteSessionStart();
                desktop.MainWindow = TryCreateMainWindow(
                    desktop,
                    windowToReplace: null);
            }

            base.OnFrameworkInitializationCompleted();
        }

        private Window TryCreateMainWindow(
            IClassicDesktopStyleApplicationLifetime desktop,
            Window? windowToReplace)
        {
            try
            {
                var mainWindow = CreateMainWindow();
                return ReplaceWindowIfNeeded(desktop, mainWindow, windowToReplace);
            }
            catch (ListenShelfDatabaseException failure)
            {
                _diagnosticLog.WriteError("Database startup failed.", failure);
                if (windowToReplace?.DataContext is DatabaseRecoveryViewModel databaseViewModel)
                {
                    databaseViewModel.ApplyFailure(failure);
                    return windowToReplace;
                }

                return ReplaceWindowIfNeeded(
                    desktop,
                    CreateRecoveryWindow(desktop, failure),
                    windowToReplace);
            }
            catch (Exception failure)
            {
                _diagnosticLog.WriteError("Application startup failed.", failure);
                if (windowToReplace?.DataContext is AppStartupErrorViewModel startupViewModel)
                {
                    startupViewModel.ApplyFailure(failure);
                    return windowToReplace;
                }

                return ReplaceWindowIfNeeded(
                    desktop,
                    CreateStartupErrorWindow(desktop, failure),
                    windowToReplace);
            }
        }

        private MainWindow CreateMainWindow()
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
            var audioEngine = new LibVlcAudioEngine();
            _diagnosticLog.WriteInfo(
                $"Database initialized at {database.DatabasePath}; playback runtime initialized for {LibVlcRuntimeLocator.RuntimeDescription}.");
            var viewModel = new MainWindowViewModel(
                audioEngine,
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

        private static Window ReplaceWindowIfNeeded(
            IClassicDesktopStyleApplicationLifetime desktop,
            Window nextWindow,
            Window? windowToReplace)
        {
            if (windowToReplace is not null)
            {
                desktop.MainWindow = nextWindow;
                nextWindow.Show();
                windowToReplace.Close();
            }

            return nextWindow;
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

        private AppStartupErrorWindow CreateStartupErrorWindow(
            IClassicDesktopStyleApplicationLifetime desktop,
            Exception failure)
        {
            var errorWindow = new AppStartupErrorWindow();
            var viewModel = new AppStartupErrorViewModel(failure, _diagnosticLog);
            errorWindow.DataContext = viewModel;
            viewModel.RetryRequested += (_, _) =>
            {
                _ = TryCreateMainWindow(desktop, errorWindow);
            };
            return errorWindow;
        }
    }
}
