using System.Globalization;
using ListenShelf.Application.Playback;
using ListenShelf.Application.Settings;
using ListenShelf.Desktop.Services;
using ListenShelf.Desktop.ViewModels;
using ListenShelf.Infrastructure.Library;
using ListenShelf.Infrastructure.Settings;
using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Tests;

public sealed class JumpToTimeTests
{
    [Theory]
    [InlineData("0:00", 0)]
    [InlineData("00:00", 0)]
    [InlineData("14:30", 870)]
    [InlineData("2:14:30", 8070)]
    [InlineData("02:14:30", 8070)]
    [InlineData("90:00", 5400)]
    [InlineData(" 1:00:01 ", 3601)]
    [InlineData("48:00:00", 172800)]
    [InlineData("100:00:00", 360000)]
    public void ParsesBookTime_IncludingLongBooksAndExactEnd(string input, int seconds)
    {
        var model = new JumpToTimeViewModel(TimeSpan.Zero, TimeSpan.FromHours(100)) { TimeText = input };
        Assert.True(model.CanJump);
        Assert.True(model.TryGetPosition(out var position));
        Assert.Equal(TimeSpan.FromSeconds(seconds), position);
        Assert.Empty(model.ValidationMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("123")]
    [InlineData("-1:00")]
    [InlineData("+1:00")]
    [InlineData("1:60")]
    [InlineData("1:60:00")]
    [InlineData("1:2:03")]
    [InlineData("1:02:3")]
    [InlineData("1:02:60")]
    [InlineData("1:02:03:04")]
    [InlineData("1:02.5")]
    [InlineData("1,5:00")]
    [InlineData("1 :00")]
    [InlineData("one:00")]
    [InlineData("١:٠٠")]
    [InlineData("18446744073709551616:00")]
    public void RejectsMalformedTimesWithoutThrowing(string input)
    {
        var model = new JumpToTimeViewModel(TimeSpan.Zero, TimeSpan.FromHours(3)) { TimeText = input };
        Assert.False(model.CanJump);
        Assert.False(model.TryGetPosition(out _));
        Assert.NotEmpty(model.ValidationMessage);
    }

    [Theory]
    [InlineData("3:00:01")]
    [InlineData("180:01")]
    [InlineData("18446744073709551615:59:59")]
    public void RejectsTimesBeyondBookLength_IncludingOverflowSizedInput(string input)
    {
        var model = new JumpToTimeViewModel(TimeSpan.Zero, TimeSpan.FromHours(3)) { TimeText = input };
        Assert.False(model.TryGetPosition(out _));
        Assert.Contains("0:00 to 3:00:00", model.ValidationMessage);
    }

    [Fact]
    public void PrefillAndParsing_AreCultureIndependent_AndDoNotWrapHours()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            var model = new JumpToTimeViewModel(TimeSpan.FromHours(49.5), TimeSpan.FromHours(60));
            Assert.Equal("49:30:00", model.TimeText);
            Assert.Equal("Book time: 0:00 – 60:00:00", model.RangeText);
            Assert.True(model.TryGetPosition(out var position));
            Assert.Equal(TimeSpan.FromHours(49.5), position);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void ValidationUpdatesWhileTyping_AndBoundsIncludeZeroAndExactEnd()
    {
        var model = new JumpToTimeViewModel(TimeSpan.FromSeconds(999), TimeSpan.FromSeconds(90.5));
        Assert.Equal("1:30", model.TimeText);
        var notifications = new List<string?>();
        model.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        model.TimeText = "1:31";
        Assert.False(model.CanJump);
        Assert.Contains(nameof(model.CanJump), notifications);
        Assert.Contains(nameof(model.ValidationMessage), notifications);
        model.TimeText = "0:00";
        Assert.True(model.CanJump);
        Assert.Equal("0:00", new JumpToTimeViewModel(TimeSpan.FromSeconds(-5), TimeSpan.FromSeconds(90)).TimeText);
        Assert.False(new JumpToTimeViewModel(TimeSpan.Zero, TimeSpan.Zero).CanJump);
        Assert.False(new JumpToTimeViewModel(TimeSpan.Zero, TimeSpan.FromSeconds(-1)).CanJump);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 8070)]
    [InlineData(true, 8070)]
    [InlineData(false, 10800)]
    public async Task Jump_SeeksOnceWithoutChangingPlayPauseState(bool playing, int targetSeconds)
    {
        using var workspace = new TestWorkspace();
        var engine = new TestEngine();
        var dialog = new TestDialog { Result = TimeSpan.FromSeconds(targetSeconds) };
        using var model = Create(workspace, engine, dialog);
        model.IsPlaying = playing;
        model.SelectedPlaybackRate = 2;
        await model.JumpToTimeCommand.ExecuteAsync(null);
        Assert.Equal(TimeSpan.FromSeconds(300), dialog.RequestedPosition);
        Assert.Equal(engine.Duration, dialog.RequestedDuration);
        Assert.Equal(TimeSpan.FromSeconds(targetSeconds), engine.Position);
        Assert.Equal(targetSeconds, model.PositionSeconds);
        Assert.Equal(1, engine.SeekCount);
        Assert.Equal(0, engine.PlayCount);
        Assert.Equal(0, engine.PauseCount);
        Assert.Equal(playing, model.IsPlaying);
    }

    [Fact]
    public async Task Cancel_DoesNotSeek_OrUndoPlaybackThatContinuedDuringDialog()
    {
        using var workspace = new TestWorkspace();
        var engine = new TestEngine();
        var completion = new TaskCompletionSource<TimeSpan?>();
        var dialog = new TestDialog { PendingResult = completion.Task };
        using var model = Create(workspace, engine, dialog);
        model.IsPlaying = true;
        var jump = model.JumpToTimeCommand.ExecuteAsync(null);
        Assert.False(model.JumpToTimeCommand.CanExecute(null));
        engine.Position = TimeSpan.FromSeconds(310);
        completion.SetResult(null);
        await jump;
        Assert.Equal(TimeSpan.FromSeconds(310), engine.Position);
        Assert.Equal(0, engine.SeekCount);
        Assert.True(model.IsPlaying);
        Assert.Equal(0, engine.PauseCount);
    }

    [Theory]
    [InlineData("reload")]
    [InlineData("busy")]
    [InlineData("dispose")]
    public async Task StaleDialogResult_CannotSeekAnotherSessionOrUnavailablePlayer(string change)
    {
        using var workspace = new TestWorkspace();
        var engine = new TestEngine();
        var completion = new TaskCompletionSource<TimeSpan?>();
        var dialog = new TestDialog { PendingResult = completion.Task };
        using var model = Create(workspace, engine, dialog);
        var jump = model.JumpToTimeCommand.ExecuteAsync(null);
        if (change == "reload") { model.IsFileLoaded = false; model.IsFileLoaded = true; }
        if (change == "busy") model.IsBusy = true;
        if (change == "dispose") model.Dispose();
        completion.SetResult(TimeSpan.FromMinutes(20));
        await jump;
        Assert.Equal(0, engine.SeekCount);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10801)]
    public async Task CommandRevalidatesDialogResult_AgainstCurrentBookLength(int targetSeconds)
    {
        using var workspace = new TestWorkspace();
        var engine = new TestEngine();
        var dialog = new TestDialog { Result = TimeSpan.FromSeconds(targetSeconds) };
        using var model = Create(workspace, engine, dialog);
        await model.JumpToTimeCommand.ExecuteAsync(null);
        Assert.Equal(0, engine.SeekCount);
        Assert.Contains("outside the audiobook", model.ErrorMessage);
    }

    [Fact]
    public async Task CommandAvailability_TracksLoadingBusyAndUnknownDuration()
    {
        using var workspace = new TestWorkspace();
        var dialog = new TestDialog();
        using var model = Create(workspace, new TestEngine(), dialog);
        var notifications = 0;
        model.JumpToTimeCommand.CanExecuteChanged += (_, _) => notifications++;
        model.IsFileLoaded = false;
        await AssertUnavailable();
        model.IsFileLoaded = true;
        model.IsBusy = true;
        await AssertUnavailable();
        model.IsBusy = false;
        foreach (var duration in new[] { 0d, -1d, double.NaN, double.PositiveInfinity })
        {
            model.DurationSeconds = duration;
            await AssertUnavailable();
        }
        model.DurationSeconds = 10800;
        Assert.True(model.JumpToTimeCommand.CanExecute(null));
        Assert.True(notifications > 0);
        Assert.Equal(0, dialog.ShowCount);

        async Task AssertUnavailable()
        {
            Assert.False(model.CanJumpToTime);
            Assert.False(model.JumpToTimeCommand.CanExecute(null));
            await model.JumpToTimeCommand.ExecuteAsync(null);
        }
    }

    [Fact]
    public async Task JumpPastChapterSleepTarget_StillHonorsTheTimer_AndUpdatesSelectedChapter()
    {
        using var workspace = new TestWorkspace();
        var engine = new TestEngine();
        var dialog = new TestDialog { Result = TimeSpan.FromSeconds(700) };
        using var model = Create(workspace, engine, dialog);
        model.Chapters.Add(new(0, "First", TimeSpan.Zero, TimeSpan.FromSeconds(600)));
        model.Chapters.Add(new(1, "Second", TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(600)));
        model.IsPlaying = true;
        model.StartChapterSleepTimerCommand.Execute(null);
        await model.JumpToTimeCommand.ExecuteAsync(null);
        Assert.False(model.IsSleepTimerActive);
        Assert.Equal(1, engine.PauseCount);
        Assert.Equal(0, engine.PlayCount);
        Assert.Equal(1, model.SelectedChapter?.Index);
        Assert.Equal(1, engine.SeekCount);
    }

    [Fact]
    public async Task SeekFailure_IsReportedWithoutChangingPlaybackState()
    {
        using var workspace = new TestWorkspace();
        var engine = new TestEngine { FailSeek = true };
        using var model = Create(workspace, engine, new TestDialog { Result = TimeSpan.FromSeconds(100) });
        await model.JumpToTimeCommand.ExecuteAsync(null);
        Assert.Contains("Could not jump", model.ErrorMessage);
        Assert.Equal(0, engine.PlayCount);
        Assert.Equal(0, engine.PauseCount);
        Assert.Equal(300, model.PositionSeconds);
    }

    private static MainWindowViewModel Create(TestWorkspace workspace, TestEngine engine, IJumpToTimeService dialog)
    {
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        var model = new MainWindowViewModel(
            engine, filePickerService: null!, progressStore: null!, bookmarkStore: null!,
            appSettingsStore: new SqliteAppSettingsStore(database), themeService: new NoOpThemeService(),
            audiobookLibrary: new SqliteAudiobookLibrary(database, workspace.ManagedLibraryPath),
            bookMetadataEditorService: null!, bookmarkEditorService: null!, bookRemovalConfirmationService: null!,
            managedLibraryIntegrityChecker: null!, managedLibraryMaintenance: null!, libraryBackupService: null!,
            managedFileVerifier: null!, managedFileRepairer: null!, jumpToTimeService: dialog, sleepTimerDurationService: null!);
        model.DurationSeconds = engine.Duration.TotalSeconds;
        model.PositionSeconds = 300;
        model.IsFileLoaded = true;
        return model;
    }

    private sealed class TestDialog : IJumpToTimeService
    {
        public TimeSpan? Result { get; init; }
        public Task<TimeSpan?>? PendingResult { get; init; }
        public TimeSpan RequestedPosition { get; private set; }
        public TimeSpan RequestedDuration { get; private set; }
        public int ShowCount { get; private set; }
        public Task<TimeSpan?> ShowAsync(TimeSpan position, TimeSpan duration)
        {
            ShowCount++;
            RequestedPosition = position;
            RequestedDuration = duration;
            return PendingResult ?? Task.FromResult(Result);
        }
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
        public TimeSpan Position { get; set; } = TimeSpan.FromSeconds(300);
        public TimeSpan Duration => TimeSpan.FromHours(3);
        public int Volume { get; set; }
        public double PlaybackRate { get; private set; } = 1;
        public IReadOnlyList<AudioChapter> Chapters => [];
        public int CurrentChapterIndex => -1;
        public int PauseCount { get; private set; }
        public int PlayCount { get; private set; }
        public int SeekCount { get; private set; }
        public bool FailSeek { get; init; }
        public Task LoadAsync(string filePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Unload() { }
        public bool Play() { PlayCount++; return true; }
        public void Pause() => PauseCount++;
        public void Stop() { }
        public void Seek(TimeSpan position)
        {
            if (FailSeek) throw new InvalidOperationException("Test seek failure");
            SeekCount++;
            Position = position;
        }
        public bool TrySetPlaybackRate(double rate) { PlaybackRate = rate; return true; }
        public bool TrySelectChapter(int chapterIndex) => true;
        public void Dispose() { }
    }
}
