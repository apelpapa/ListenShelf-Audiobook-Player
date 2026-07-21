using Avalonia.Controls;
using ListenShelf.Application.Library;
using ListenShelf.Application.Progress;
using ListenShelf.Application.Settings;
using ListenShelf.Desktop.ViewModels;
using ListenShelf.Desktop.Views;
using ListenShelf.Playback.LibVlc;

namespace ListenShelf.Desktop.Services;

public sealed class AvaloniaTemporaryPlayerSessionService(
    Window owner,
    IThemeService themeService,
    IBookMetadataProvider metadataProvider) : ITemporaryPlayerSessionService
{
    public async Task<bool> WarnAndOpenAsync(AppTheme currentTheme)
    {
        var warning = new ManagedModePlayerOnlyWarningWindow();
        var shouldOpen = await warning.ShowDialog<bool>(owner);
        if (!shouldOpen)
        {
            return false;
        }

        var sessionWindow = new MainWindow();
        var viewModel = new MainWindowViewModel(
            new LibVlcAudioEngine(),
            new AvaloniaFilePickerService(sessionWindow),
            new TemporaryPlaybackProgressStore(),
            new TemporaryLibrarySettingsStore(),
            new TemporaryAppSettingsStore(currentTheme),
            themeService,
            new TemporaryAudiobookLibrary(),
            new AvaloniaBookMetadataEditorService(sessionWindow, metadataProvider),
            this,
            isTemporarySession: true);

        sessionWindow.DataContext = viewModel;
        sessionWindow.Closed += (_, _) => viewModel.Dispose();
        sessionWindow.Show(owner);
        return true;
    }

    private sealed class TemporaryPlaybackProgressStore : IPlaybackProgressStore
    {
        public PlaybackProgress? Get(string filePath) => null;

        public void Save(PlaybackProgress progress)
        {
        }
    }

    private sealed class TemporaryLibrarySettingsStore : ILibrarySettingsStore
    {
        public LibraryStorageMode? GetDefaultStorageMode() => LibraryStorageMode.Linked;

        public void SaveDefaultStorageMode(LibraryStorageMode storageMode)
        {
        }
    }

    private sealed class TemporaryAppSettingsStore(AppTheme theme) : IAppSettingsStore
    {
        public AppTheme GetTheme() => theme;

        public void SaveTheme(AppTheme value)
        {
        }

        public LibraryViewMode GetLibraryViewMode() => LibraryViewMode.List;

        public void SaveLibraryViewMode(LibraryViewMode viewMode)
        {
        }

        public LibraryGroupMode GetLibraryGroupMode() => LibraryGroupMode.None;

        public void SaveLibraryGroupMode(LibraryGroupMode groupMode)
        {
        }

        public double GetLibraryTileWidth() => 220d;

        public void SaveLibraryTileWidth(double tileWidth)
        {
        }
    }

    private sealed class TemporaryAudiobookLibrary : IAudiobookLibrary
    {
        public string ManagedLibraryPath => string.Empty;

        public IReadOnlyList<LibraryBook> GetBooks() => [];

        public LibraryImportResult Import(string sourceFilePath, LibraryStorageMode storageMode) =>
            throw new NotSupportedException("Temporary Player Only sessions do not create library entries.");

        public LibraryBook SetCover(Guid bookId, string sourceImagePath) =>
            throw new NotSupportedException("Temporary Player Only sessions do not save cover artwork.");

        public LibraryBook SetCover(Guid bookId, ReadOnlyMemory<byte> imageData, string fileExtension) =>
            throw new NotSupportedException("Temporary Player Only sessions do not save cover artwork.");

        public LibraryBook UpdateMetadata(Guid bookId, AudiobookMetadata metadata) =>
            throw new NotSupportedException("Temporary Player Only sessions do not save metadata.");
    }
}
