using ListenShelf.Application.Playback;
using ListenShelf.Application.Settings;
using ListenShelf.Desktop.Services;
using ListenShelf.Desktop.ViewModels;
using ListenShelf.Infrastructure.Library;
using ListenShelf.Infrastructure.Settings;
using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Tests;

public sealed class PlaybackSkipCommandTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PlayerAndExternalControls_UseLiveIntervalsAndRespectBookBoundaries(
        bool useExternalControl,
        bool forward)
    {
        using var workspace = new TestWorkspace();
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        var settings = new SqliteAppSettingsStore(database);
        settings.SaveRewindSeconds(12);
        settings.SaveForwardSeconds(45);
        var engine = new RecordingAudioEngine();
        // These commands use only the engine, settings, and catalog. Unrelated
        // dialogs and operations are intentionally not available in this test.
        using var viewModel = new MainWindowViewModel(
            audioEngine: engine,
            filePickerService: null!,
            progressStore: null!,
            bookmarkStore: null!,
            appSettingsStore: settings,
            themeService: new NoOpThemeService(),
            audiobookLibrary: new SqliteAudiobookLibrary(database, workspace.ManagedLibraryPath),
            bookMetadataEditorService: null!,
            bookmarkEditorService: null!,
            bookRemovalConfirmationService: null!,
            managedLibraryIntegrityChecker: null!,
            managedLibraryMaintenance: null!,
            libraryBackupService: null!,
            managedFileVerifier: null!, managedFileRepairer: null!, jumpToTimeService: null!);
        viewModel.IsFileLoaded = true;

        Skip();
        Assert.Equal(TimeSpan.FromSeconds(forward ? 145 : 88), engine.Position);

        viewModel.SkipSettings.RewindSeconds = 20;
        viewModel.SkipSettings.ForwardSeconds = 60;
        Skip();
        Assert.Equal(TimeSpan.FromSeconds(forward ? 205 : 68), engine.Position);

        engine.Position = TimeSpan.FromSeconds(forward ? 990 : 5);
        Skip();
        Assert.Equal(forward ? engine.Duration : TimeSpan.Zero, engine.Position);

        var seekCount = engine.SeekCount;
        viewModel.IsBusy = true;
        Skip();
        viewModel.IsBusy = false;
        viewModel.IsFileLoaded = false;
        Skip();
        Assert.Equal(seekCount, engine.SeekCount);

        void Skip()
        {
            if (useExternalControl)
            {
                // Both keyboard shortcuts and Windows media keys enter here.
                Assert.Equal(viewModel.CanControlPlayback, viewModel.TryHandlePlaybackControl(
                    forward ? PlaybackControlAction.SkipForward : PlaybackControlAction.SkipBackward));
            }
            else
            {
                (forward ? viewModel.SkipForwardCommand : viewModel.SkipBackwardCommand).Execute(null);
            }
        }
    }

    private sealed class NoOpThemeService : IThemeService
    {
        public void ApplyTheme(AppTheme theme) { }
    }

    private sealed class RecordingAudioEngine : IAudioEngine
    {
        public event EventHandler<PlaybackProgressChangedEventArgs>? ProgressChanged { add { } remove { } }
        public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged { add { } remove { } }
        public event EventHandler<PlaybackChaptersChangedEventArgs>? ChaptersChanged { add { } remove { } }

        public string? CurrentFilePath => null;
        public TimeSpan Position { get; set; } = TimeSpan.FromSeconds(100);
        public TimeSpan Duration => TimeSpan.FromSeconds(1000);
        public int Volume { get; set; }
        public double PlaybackRate { get; private set; } = 1;
        public IReadOnlyList<AudioChapter> Chapters => [];
        public int CurrentChapterIndex => -1;
        public int SeekCount { get; private set; }

        public void Seek(TimeSpan position)
        {
            SeekCount++;
            Position = position;
        }

        public bool TrySetPlaybackRate(double rate)
        {
            PlaybackRate = rate;
            return true;
        }

        public Task LoadAsync(string filePath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public void Unload() => throw new NotSupportedException();
        public bool Play() => throw new NotSupportedException();
        public void Pause() => throw new NotSupportedException();
        public void Stop() => throw new NotSupportedException();
        public bool TrySelectChapter(int chapterIndex) => throw new NotSupportedException();
        public void Dispose() { }
    }
}
