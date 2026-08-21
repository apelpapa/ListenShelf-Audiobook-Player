using Avalonia.Input;
using ListenShelf.Application.Playback;
using ListenShelf.Application.Settings;
using ListenShelf.Desktop.Services;
using ListenShelf.Desktop.ViewModels;
using ListenShelf.Infrastructure.Library;
using ListenShelf.Infrastructure.Settings;
using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Tests;

public sealed class PlaybackMuteTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ButtonAndKeyboardRouting_RestoreVolumeWithoutChangingPlaybackOrSavedSetting(bool keyboard, bool playing)
    {
        using var workspace = new TestWorkspace();
        var settings = Settings(workspace);
        settings.SavePlaybackVolume(37);
        var engine = new TestEngine();
        using var model = Create(workspace, settings, engine);
        model.IsFileLoaded = true;
        model.IsPlaying = playing;

        Toggle();
        Assert.True(model.IsMuted);
        Assert.False(model.IsVolumeAudible);
        Assert.Equal("Unmute (M)", model.MuteButtonToolTip);
        Assert.Equal(0, engine.Volume);
        Assert.Equal(37, model.Volume);
        Assert.Equal(37, settings.GetPlaybackVolume());

        Toggle();
        Assert.False(model.IsMuted);
        Assert.Equal("Mute (M)", model.MuteButtonToolTip);
        Assert.Equal(37, engine.Volume);
        Assert.Equal(playing, model.IsPlaying);
        // The test engine throws for all playback/seek operations.

        void Toggle()
        {
            if (keyboard)
            {
                var action = PlaybackKeyboardShortcuts.GetAction(Key.M, KeyModifiers.None, PlaybackKeyboardFocus.Other);
                Assert.Equal(PlaybackControlAction.ToggleMute, action);
                Assert.True(model.TryHandlePlaybackControl(action!.Value));
            }
            else model.ToggleMuteCommand.Execute(null);
        }
    }

    [Fact]
    public void AdjustingSlider_UnmutesAndSavesNewLevel_AndZeroCanRestoreLastAudibleLevel()
    {
        using var workspace = new TestWorkspace();
        var settings = Settings(workspace);
        var engine = new TestEngine();
        using var model = Create(workspace, settings, engine);
        model.ToggleMuteCommand.Execute(null);
        model.Volume = 23;
        Assert.False(model.IsMuted);
        Assert.Equal(23, engine.Volume);
        Assert.Equal(23, settings.GetPlaybackVolume());
        model.Volume = 0;
        Assert.True(model.IsMuted);
        Assert.Equal(0, engine.Volume);
        model.ToggleMuteCommand.Execute(null);
        Assert.Equal(23, model.Volume);
        Assert.Equal(23, engine.Volume);
        Assert.False(model.IsMuted);

        model.ToggleMuteCommand.Execute(null);
        model.Volume = 0;
        model.ToggleMuteCommand.Execute(null);
        Assert.Equal(23, engine.Volume);
    }

    [Theory]
    [InlineData(0, 80)]
    [InlineData(0.4, 80)]
    [InlineData(35.7, 36)]
    public void StartupUsesSavedLevel_AndSilentLevelsHaveSafeUnmuteFallback(double saved, int unmuted)
    {
        using var workspace = new TestWorkspace();
        var settings = Settings(workspace);
        settings.SavePlaybackVolume(saved);
        var engine = new TestEngine();
        using var model = Create(workspace, settings, engine);
        Assert.Equal((int)Math.Round(saved), engine.Volume);
        if (!model.IsMuted) model.ToggleMuteCommand.Execute(null);
        model.ToggleMuteCommand.Execute(null);
        Assert.False(model.IsMuted);
        Assert.Equal(unmuted, engine.Volume);
    }

    [Fact]
    public void MuteSurvivesBookChangesButNotApplicationRestart()
    {
        using var workspace = new TestWorkspace();
        var settings = Settings(workspace);
        settings.SavePlaybackVolume(42);
        using (var model = Create(workspace, settings, new TestEngine()))
        {
            model.ToggleMuteCommand.Execute(null);
            model.IsFileLoaded = true;
            model.IsFileLoaded = false;
            model.IsFileLoaded = true;
            Assert.True(model.IsMuted);
        }
        var reopenedEngine = new TestEngine();
        using var reopened = Create(workspace, settings, reopenedEngine);
        Assert.False(reopened.IsMuted);
        Assert.Equal(42, reopenedEngine.Volume);
    }

    [Fact]
    public void MuteWorksWithoutALoadedBook_AndDoesNotTouchADisposedEngine()
    {
        using var workspace = new TestWorkspace();
        var engine = new TestEngine();
        using var model = Create(workspace, Settings(workspace), engine);
        Assert.True(model.ToggleMuteCommand.CanExecute(null));
        Assert.True(model.TryHandlePlaybackControl(PlaybackControlAction.ToggleMute));
        Assert.Equal(0, engine.Volume);
        model.IsBusy = true;
        Assert.True(model.TryHandlePlaybackControl(PlaybackControlAction.ToggleMute));
        Assert.Equal(80, engine.Volume);
        model.Dispose();
        Assert.False(model.ToggleMuteCommand.CanExecute(null));
        Assert.False(model.TryHandlePlaybackControl(PlaybackControlAction.ToggleMute));
        model.ToggleMuteCommand.Execute(null);
        Assert.Equal(80, engine.Volume);
    }

    [Fact]
    public void MuteStateChanges_NotifyIconTooltipAndCommandBindings()
    {
        using var workspace = new TestWorkspace();
        using var model = Create(workspace, Settings(workspace), new TestEngine());
        var notifications = new List<string?>();
        var commandChanges = 0;
        model.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        model.ToggleMuteCommand.CanExecuteChanged += (_, _) => commandChanges++;
        model.ToggleMuteCommand.Execute(null);
        Assert.Contains(nameof(model.IsMuted), notifications);
        Assert.Contains(nameof(model.IsVolumeAudible), notifications);
        Assert.Contains(nameof(model.MuteButtonToolTip), notifications);
        notifications.Clear();
        model.Volume = 20;
        Assert.Contains(nameof(model.IsMuted), notifications);
        Assert.False(model.IsMuted);
        model.Dispose();
        Assert.True(commandChanges > 0);
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(110, 100)]
    [InlineData(double.NaN, 80)]
    [InlineData(double.PositiveInfinity, 80)]
    public void VolumeValidationStillAppliesWhileMuted(double input, int expected)
    {
        using var workspace = new TestWorkspace();
        var settings = Settings(workspace);
        var engine = new TestEngine();
        using var model = Create(workspace, settings, engine);
        model.ToggleMuteCommand.Execute(null);
        model.Volume = input;
        Assert.Equal(expected, model.Volume);
        Assert.Equal(expected, engine.Volume);
        Assert.Equal(expected == 0, model.IsMuted);
        Assert.Equal(expected, settings.GetPlaybackVolume());
    }

    [Theory]
    [InlineData(PlaybackKeyboardFocus.Other)]
    [InlineData(PlaybackKeyboardFocus.Button)]
    [InlineData(PlaybackKeyboardFocus.Slider)]
    public void MShortcutWorksOnNonTextControls(PlaybackKeyboardFocus focus)
    {
        Assert.Equal(PlaybackControlAction.ToggleMute,
            PlaybackKeyboardShortcuts.GetAction(Key.M, KeyModifiers.None, focus));
    }

    [Theory]
    [InlineData(KeyModifiers.None, PlaybackKeyboardFocus.TextInput)]
    [InlineData(KeyModifiers.None, PlaybackKeyboardFocus.ComboBox)]
    [InlineData(KeyModifiers.Control, PlaybackKeyboardFocus.Other)]
    [InlineData(KeyModifiers.Alt, PlaybackKeyboardFocus.Other)]
    [InlineData(KeyModifiers.Shift, PlaybackKeyboardFocus.Other)]
    [InlineData(KeyModifiers.Meta, PlaybackKeyboardFocus.Other)]
    public void MShortcutDoesNotInterceptTypingSelectionOrModifiedKeys(KeyModifiers modifiers, PlaybackKeyboardFocus focus)
    {
        Assert.Null(PlaybackKeyboardShortcuts.GetAction(Key.M, modifiers, focus));
    }

    [Theory]
    [InlineData(Key.Space, PlaybackKeyboardFocus.Other, PlaybackControlAction.TogglePlayPause)]
    [InlineData(Key.K, PlaybackKeyboardFocus.Other, PlaybackControlAction.TogglePlayPause)]
    [InlineData(Key.Left, PlaybackKeyboardFocus.Other, PlaybackControlAction.SkipBackward)]
    [InlineData(Key.J, PlaybackKeyboardFocus.Other, PlaybackControlAction.SkipBackward)]
    [InlineData(Key.Right, PlaybackKeyboardFocus.Other, PlaybackControlAction.SkipForward)]
    [InlineData(Key.L, PlaybackKeyboardFocus.Other, PlaybackControlAction.SkipForward)]
    [InlineData(Key.Space, PlaybackKeyboardFocus.Button, null)]
    [InlineData(Key.K, PlaybackKeyboardFocus.TextInput, null)]
    [InlineData(Key.Left, PlaybackKeyboardFocus.Slider, null)]
    [InlineData(Key.Right, PlaybackKeyboardFocus.ComboBox, null)]
    public void ExistingKeyboardShortcutsAndFocusExceptionsRemainUnchanged(
        Key key, PlaybackKeyboardFocus focus, PlaybackControlAction? expected)
    {
        Assert.Equal(expected, PlaybackKeyboardShortcuts.GetAction(key, KeyModifiers.None, focus));
    }

    private static SqliteAppSettingsStore Settings(TestWorkspace workspace) =>
        new(new ListenShelfDatabase(workspace.DatabasePath));

    private static MainWindowViewModel Create(TestWorkspace workspace, IAppSettingsStore settings, TestEngine engine) =>
        new(engine, filePickerService: null!, progressStore: null!, bookmarkStore: null!,
            appSettingsStore: settings, themeService: new NoOpThemeService(),
            audiobookLibrary: new SqliteAudiobookLibrary(new ListenShelfDatabase(workspace.DatabasePath), workspace.ManagedLibraryPath),
            bookMetadataEditorService: null!, bookmarkEditorService: null!, bookRemovalConfirmationService: null!,
            managedLibraryIntegrityChecker: null!, managedLibraryMaintenance: null!, libraryBackupService: null!,
            managedFileVerifier: null!, managedFileRepairer: null!, jumpToTimeService: null!);

    private sealed class NoOpThemeService : IThemeService
    {
        public void ApplyTheme(AppTheme theme) { }
    }

    private sealed class TestEngine : IAudioEngine
    {
        private int _volume;
        private bool _disposed;
        public int Volume
        {
            get => _volume;
            set { ObjectDisposedException.ThrowIf(_disposed, this); _volume = value; }
        }
        public event EventHandler<PlaybackProgressChangedEventArgs>? ProgressChanged { add { } remove { } }
        public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged { add { } remove { } }
        public event EventHandler<PlaybackChaptersChangedEventArgs>? ChaptersChanged { add { } remove { } }
        public string? CurrentFilePath => null;
        public TimeSpan Position => TimeSpan.Zero;
        public TimeSpan Duration => TimeSpan.Zero;
        public double PlaybackRate => 1;
        public IReadOnlyList<AudioChapter> Chapters => [];
        public int CurrentChapterIndex => -1;
        public Task LoadAsync(string filePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Unload() => throw new NotSupportedException();
        public bool Play() => throw new NotSupportedException();
        public void Pause() => throw new NotSupportedException();
        public void Stop() => throw new NotSupportedException();
        public void Seek(TimeSpan position) => throw new NotSupportedException();
        public bool TrySetPlaybackRate(double rate) => true;
        public bool TrySelectChapter(int chapterIndex) => throw new NotSupportedException();
        public void Dispose() => _disposed = true;
    }
}
