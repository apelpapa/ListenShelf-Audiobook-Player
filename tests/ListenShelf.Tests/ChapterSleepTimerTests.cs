using ListenShelf.Application.Playback;
using ListenShelf.Application.Settings;
using ListenShelf.Desktop.Services;
using ListenShelf.Desktop.ViewModels;
using ListenShelf.Infrastructure.Library;
using ListenShelf.Infrastructure.Settings;
using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Tests;

public sealed class ChapterSleepTimerTests
{
    [Theory]
    [InlineData(0.75)]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void PausesOnceAtCapturedChapterEnd_RegardlessOfSpeed(double rate)
    {
        using var workspace = new TestWorkspace();
        var engine = new TestEngine();
        using var model = Create(workspace, engine);
        model.StartChapterSleepTimerCommand.Execute(null);
        Assert.True(model.IsSleepTimerActive);
        Assert.True(model.IsChapterSleepActive);
        Assert.Equal("Sleep • chapter 2", model.SleepTimerButtonText);
        Assert.Contains("end of chapter 2", model.SleepTimerStatusText);

        model.SelectedPlaybackRate = rate;
        model.PositionSeconds = 1199.9;
        Assert.Equal(0, engine.PauseCount);
        model.PositionSeconds = 1200;
        Assert.Equal(1, engine.PauseCount);
        Assert.False(model.IsSleepTimerActive);
        Assert.False(model.IsChapterSleepActive);
        Assert.Equal("Sleep timer", model.SleepTimerButtonText);
        model.PositionSeconds = 1300;
        Assert.Equal(1, engine.PauseCount);
    }

    [Fact]
    public void PauseAndRewind_KeepTheOriginalTarget_UntilItIsReached()
    {
        using var workspace = new TestWorkspace();
        var engine = new TestEngine();
        using var model = Create(workspace, engine);
        model.IsPlaying = false;
        model.StartChapterSleepTimerCommand.Execute(null);
        Assert.True(model.IsChapterSleepActive); // May be armed before Play.
        model.PositionSeconds = 300;
        Assert.True(model.IsChapterSleepActive);
        Assert.Contains("chapter 2", model.SleepTimerStatusText);
        model.IsPlaying = true;
        model.PositionSeconds = 600; // Passing the earlier chapter's end must not pause.
        Assert.Equal(0, engine.PauseCount);
        model.PositionSeconds = 1200;
        Assert.Equal(1, engine.PauseCount);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void SeekingPastTarget_FinishesTimer_WithoutStartingPausedPlayback(bool playing, int expectedPauses)
    {
        using var workspace = new TestWorkspace();
        var engine = new TestEngine();
        using var model = Create(workspace, engine);
        model.IsPlaying = playing;
        model.StartChapterSleepTimerCommand.Execute(null);
        model.PositionSeconds = 1500;
        Assert.False(model.IsSleepTimerActive);
        Assert.Equal(expectedPauses, engine.PauseCount);
        Assert.Equal(0, engine.PlayCount);
    }

    [Fact]
    public void ChapterSelectionAndMetadataRefresh_DoNotMoveTheArmedTarget()
    {
        using var workspace = new TestWorkspace();
        var engine = new TestEngine();
        using var model = Create(workspace, engine);
        model.StartChapterSleepTimerCommand.Execute(null);
        model.SelectedChapter = model.Chapters[2]; // Engine selection can lead the progress event.
        model.Chapters[1] = Chapter(1, 600, 300);
        model.PositionSeconds = 1000;
        Assert.True(model.IsChapterSleepActive);
        Assert.Equal(0, engine.PauseCount);
        model.PositionSeconds = 1200;
        Assert.Equal(1, engine.PauseCount);
    }

    [Fact]
    public void CancelAndChangingBooks_ClearChapterTimer()
    {
        using var workspace = new TestWorkspace();
        var engine = new TestEngine();
        using var model = Create(workspace, engine);
        model.StartChapterSleepTimerCommand.Execute(null);
        model.CancelSleepTimerCommand.Execute(null);
        Assert.False(model.IsChapterSleepActive);
        model.PositionSeconds = 1200;
        Assert.Equal(0, engine.PauseCount);
        model.PositionSeconds = 700;
        model.StartChapterSleepTimerCommand.Execute(null);
        model.IsFileLoaded = false;
        Assert.False(model.IsSleepTimerActive);
        model.IsFileLoaded = true;
        model.PositionSeconds = 1500;
        Assert.Equal(0, engine.PauseCount);
    }

    [Fact]
    public void SwitchingTimerModes_ReplacesTheOldMode_AndAddTenOnlyWorksForMinutes()
    {
        using var workspace = new TestWorkspace();
        var engine = new TestEngine();
        using var model = Create(workspace, engine);
        model.StartSleepTimer15Command.Execute(null);
        Assert.True(model.CanAddTenMinutesToSleepTimer);
        model.AddTenMinutesToSleepTimerCommand.Execute(null);
        Assert.True(model.SleepTimerRemaining > TimeSpan.FromMinutes(24));
        model.StartChapterSleepTimerCommand.Execute(null);
        Assert.False(model.CanAddTenMinutesToSleepTimer);
        Assert.False(model.AddTenMinutesToSleepTimerCommand.CanExecute(null));
        model.AddTenMinutesToSleepTimerCommand.Execute(null);
        Assert.True(model.IsChapterSleepActive);
        Assert.Equal(TimeSpan.Zero, model.SleepTimerRemaining);
        model.StartSleepTimer30Command.Execute(null);
        Assert.False(model.IsChapterSleepActive);
        Assert.True(model.CanAddTenMinutesToSleepTimer);
        model.PositionSeconds = 1500;
        Assert.Equal(0, engine.PauseCount);
        Assert.True(model.IsSleepTimerActive);
        model.CancelSleepTimerCommand.Execute(null);
        Assert.False(model.IsSleepTimerActive);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingDuration_CanUseNextChapterOrBookEnd(bool lastChapter)
    {
        using var workspace = new TestWorkspace();
        var engine = new TestEngine();
        using var model = Create(workspace, engine);
        model.Chapters[1] = Chapter(1, 600, 0);
        model.Chapters[2] = Chapter(2, 1200, 0);
        if (lastChapter) model.PositionSeconds = 1300;
        Assert.True(model.CanStopAtChapterEnd);
        model.StartChapterSleepTimerCommand.Execute(null);
        model.PositionSeconds = lastChapter ? 1800 : 1200;
        Assert.Equal(1, engine.PauseCount);
        Assert.False(model.IsSleepTimerActive);
    }

    [Fact]
    public void UnknownChapterTimingOrBusyPlayer_CannotArmTimer()
    {
        using var workspace = new TestWorkspace();
        var engine = new TestEngine();
        using var model = Create(workspace, engine);
        model.IsBusy = true;
        AssertUnavailable();
        model.IsBusy = false;
        model.IsFileLoaded = false;
        AssertUnavailable();
        model.IsFileLoaded = true;
        model.Chapters.Clear();
        AssertUnavailable();
        model.Chapters.Add(Chapter(0, 0, 0));
        model.DurationSeconds = 0;
        AssertUnavailable();
        model.DurationSeconds = 1800;
        model.Chapters.Add(Chapter(1, 0, 0));
        AssertUnavailable(); // Duplicate offsets are ambiguous.
        model.Chapters.Clear();
        model.Chapters.Add(Chapter(0, 0, 1800));
        model.PositionSeconds = 1800;
        AssertUnavailable(); // Already at the end.

        void AssertUnavailable()
        {
            Assert.False(model.CanStopAtChapterEnd);
            Assert.False(model.StartChapterSleepTimerCommand.CanExecute(null));
            model.StartChapterSleepTimerCommand.Execute(null);
            Assert.False(model.IsSleepTimerActive);
        }
    }

    [Fact]
    public void AvailabilityAndModeLabels_NotifyBindings_AndStateIsNotPersisted()
    {
        using var workspace = new TestWorkspace();
        using (var model = Create(workspace, new TestEngine()))
        {
            var notifications = new List<string?>();
            var canExecuteChanges = 0;
            model.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
            model.StartChapterSleepTimerCommand.CanExecuteChanged += (_, _) => canExecuteChanges++;
            model.PositionSeconds = 800;
            Assert.Contains(nameof(model.CanStopAtChapterEnd), notifications);
            Assert.True(canExecuteChanges > 0);
            notifications.Clear();
            model.StartChapterSleepTimerCommand.Execute(null);
            Assert.Contains(nameof(model.IsChapterSleepActive), notifications);
            Assert.Contains(nameof(model.SleepTimerButtonText), notifications);
            Assert.Contains(nameof(model.SleepTimerStatusText), notifications);
            Assert.Contains(nameof(model.CanAddTenMinutesToSleepTimer), notifications);
        }

        using var reopened = Create(workspace, new TestEngine());
        Assert.False(reopened.IsSleepTimerActive);
        Assert.False(reopened.IsChapterSleepActive);
    }

    private static PlaybackChapterItemViewModel Chapter(int index, double start, double duration) =>
        new(index, $"Chapter {index + 1}", TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(duration));

    private static MainWindowViewModel Create(TestWorkspace workspace, TestEngine engine)
    {
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        var model = new MainWindowViewModel(
            engine, filePickerService: null!, progressStore: null!, bookmarkStore: null!,
            appSettingsStore: new SqliteAppSettingsStore(database), themeService: new NoOpThemeService(),
            audiobookLibrary: new SqliteAudiobookLibrary(database, workspace.ManagedLibraryPath),
            bookMetadataEditorService: null!, bookmarkEditorService: null!, bookRemovalConfirmationService: null!,
            managedLibraryIntegrityChecker: null!, managedLibraryMaintenance: null!, libraryBackupService: null!,
            managedFileVerifier: null!, managedFileRepairer: null!, jumpToTimeService: null!);
        model.Chapters.Add(Chapter(0, 0, 600));
        model.Chapters.Add(Chapter(1, 600, 600));
        model.Chapters.Add(Chapter(2, 1200, 600));
        model.DurationSeconds = 1800;
        model.PositionSeconds = 700;
        engine.Position = TimeSpan.FromSeconds(700);
        model.IsFileLoaded = model.IsPlaying = true;
        return model;
    }

    private sealed class NoOpThemeService : IThemeService
    {
        public void ApplyTheme(AppTheme theme) { }
    }

    private sealed class TestEngine : IAudioEngine
    {
        public event EventHandler<PlaybackProgressChangedEventArgs>? ProgressChanged { add { } remove { } }
        public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged { add { } remove { } }
        public event EventHandler<PlaybackChaptersChangedEventArgs>? ChaptersChanged { add { } remove { } }
        public string? CurrentFilePath => null;
        public TimeSpan Position { get; set; }
        public TimeSpan Duration => TimeSpan.FromMinutes(30);
        public int Volume { get; set; }
        public double PlaybackRate { get; private set; } = 1;
        public IReadOnlyList<AudioChapter> Chapters => [];
        public int CurrentChapterIndex => -1;
        public int PauseCount { get; private set; }
        public int PlayCount { get; private set; }
        public Task LoadAsync(string filePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Unload() { }
        public bool Play() { PlayCount++; return true; }
        public void Pause() => PauseCount++;
        public void Stop() { }
        public void Seek(TimeSpan position) => Position = position;
        public bool TrySetPlaybackRate(double rate) { PlaybackRate = rate; return true; }
        public bool TrySelectChapter(int chapterIndex) => true;
        public void Dispose() { }
    }
}
