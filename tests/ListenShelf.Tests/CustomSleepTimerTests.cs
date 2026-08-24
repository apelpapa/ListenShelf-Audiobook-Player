using System.Globalization;
using ListenShelf.Application.Playback;
using ListenShelf.Application.Settings;
using ListenShelf.Desktop.Services;
using ListenShelf.Desktop.ViewModels;
using ListenShelf.Infrastructure.Library;
using ListenShelf.Infrastructure.Settings;
using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Tests;

public sealed class CustomSleepTimerTests
{
    [Theory]
    [InlineData("1", 1)]
    [InlineData("35", 35)]
    [InlineData("1440", 1440)]
    [InlineData(" 90 ", 90)]
    [InlineData("0005", 5)]
    public void AcceptsWholeMinutesIncludingBounds(string input, int expected)
    {
        var model = new CustomSleepTimerViewModel { MinutesText = input };
        Assert.True(model.CanStart);
        Assert.True(model.TryGetMinutes(out var minutes));
        Assert.Equal(expected, minutes);
        Assert.Empty(model.ValidationMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1441")]
    [InlineData("2.5")]
    [InlineData("1e2")]
    [InlineData("1,000")]
    [InlineData("+5")]
    [InlineData("90:00")]
    [InlineData("five")]
    [InlineData("2147483648")]
    [InlineData("٣٥")]
    public void RejectsInvalidDurationInsteadOfClampingOrStarting(string? input)
    {
        var model = new CustomSleepTimerViewModel { MinutesText = input! };
        Assert.False(model.CanStart);
        Assert.False(model.TryGetMinutes(out _));
        Assert.Contains("whole number", model.ValidationMessage);
    }

    [Fact]
    public void ValidationUpdatesWhileTyping_AndUsesInvariantWholeMinutes()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var model = new CustomSleepTimerViewModel();
            Assert.Equal("30", model.MinutesText);
            Assert.True(model.CanStart);
            var notifications = new List<string?>();
            model.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
            model.MinutesText = "2,5";
            Assert.False(model.CanStart);
            Assert.Contains(nameof(model.CanStart), notifications);
            Assert.Contains(nameof(model.ValidationMessage), notifications);
            model.MinutesText = "125";
            Assert.True(model.TryGetMinutes(out var minutes));
            Assert.Equal(125, minutes);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(37, false)]
    [InlineData(1440, false)]
    [InlineData(1, true)]
    [InlineData(37, true)]
    [InlineData(1440, true)]
    public async Task ValidResult_StartsCountdownWithoutChangingPlayback(int minutes, bool playing)
    {
        using var workspace = new TestWorkspace();
        using var model = Create(workspace, new TestDialog { Result = minutes });
        model.IsPlaying = playing;
        model.SelectedPlaybackRate = 2;
        await model.StartCustomSleepTimerCommand.ExecuteAsync(null);
        Assert.True(model.IsSleepTimerActive);
        Assert.False(model.IsChapterSleepActive);
        Assert.True(model.CanAddTenMinutesToSleepTimer);
        Assert.Equal(TimeSpan.FromMinutes(minutes), model.SleepTimerRemaining);
        Assert.Equal(playing, model.IsPlaying);
        // Every Play/Pause/Seek operation on the fake engine throws.
        Assert.Empty(model.ErrorMessage);
    }

    [Fact]
    public async Task CustomPresetAndChapterModes_ReplaceEachOther_AndAddTenStillWorks()
    {
        using var workspace = new TestWorkspace();
        using var model = Create(workspace, new TestDialog { Result = 37 });
        model.StartSleepTimer15Command.Execute(null);
        await model.StartCustomSleepTimerCommand.ExecuteAsync(null);
        Assert.Equal(TimeSpan.FromMinutes(37), model.SleepTimerRemaining);
        model.AddTenMinutesToSleepTimerCommand.Execute(null);
        Assert.InRange(model.SleepTimerRemaining.TotalMinutes, 46.9, 47);
        model.StartChapterSleepTimerCommand.Execute(null);
        Assert.True(model.IsChapterSleepActive);
        await model.StartCustomSleepTimerCommand.ExecuteAsync(null);
        Assert.False(model.IsChapterSleepActive);
        Assert.Equal(TimeSpan.FromMinutes(37), model.SleepTimerRemaining);
        model.StartSleepTimer30Command.Execute(null);
        Assert.Equal(TimeSpan.FromMinutes(30), model.SleepTimerRemaining);
        model.CancelSleepTimerCommand.Execute(null);
        Assert.False(model.IsSleepTimerActive);
        Assert.Equal(TimeSpan.Zero, model.SleepTimerRemaining);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("minutes")]
    [InlineData("chapter")]
    public async Task OpeningAndCancellingDialog_LeaveCurrentTimerAlone(string timerMode)
    {
        using var workspace = new TestWorkspace();
        var completion = new TaskCompletionSource<int?>();
        using var model = Create(workspace, new TestDialog { PendingResult = completion.Task });
        if (timerMode == "minutes") model.StartSleepTimer45Command.Execute(null);
        if (timerMode == "chapter") model.StartChapterSleepTimerCommand.Execute(null);
        var remaining = model.SleepTimerRemaining;
        var label = model.SleepTimerButtonText;
        var command = model.StartCustomSleepTimerCommand.ExecuteAsync(null);
        Assert.Equal(timerMode != "none", model.IsSleepTimerActive);
        Assert.Equal(label, model.SleepTimerButtonText);
        Assert.False(model.StartCustomSleepTimerCommand.CanExecute(null));
        completion.SetResult(null);
        await command;
        Assert.Equal(timerMode != "none", model.IsSleepTimerActive);
        Assert.Equal(timerMode == "chapter", model.IsChapterSleepActive);
        Assert.Equal(remaining, model.SleepTimerRemaining);
        Assert.Equal(label, model.SleepTimerButtonText);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1441)]
    [InlineData(int.MaxValue)]
    public async Task CommandRevalidatesResult_WithoutReplacingExistingTimer(int minutes)
    {
        using var workspace = new TestWorkspace();
        using var model = Create(workspace, new TestDialog { Result = minutes });
        model.StartChapterSleepTimerCommand.Execute(null);
        await model.StartCustomSleepTimerCommand.ExecuteAsync(null);
        Assert.True(model.IsChapterSleepActive);
        Assert.True(model.IsSleepTimerActive);
        Assert.Contains("whole minutes", model.ErrorMessage);
    }

    [Theory]
    [InlineData("unload")]
    [InlineData("reload")]
    [InlineData("busy")]
    [InlineData("dispose")]
    public async Task StaleDialogCannotStartTimerForAnUnavailableOrChangedSession(string change)
    {
        using var workspace = new TestWorkspace();
        var completion = new TaskCompletionSource<int?>();
        using var model = Create(workspace, new TestDialog { PendingResult = completion.Task });
        var command = model.StartCustomSleepTimerCommand.ExecuteAsync(null);
        if (change is "unload" or "reload") model.IsFileLoaded = false;
        if (change == "reload") model.IsFileLoaded = true;
        if (change == "busy") model.IsBusy = true;
        if (change == "dispose") model.Dispose();
        completion.SetResult(37);
        await command;
        Assert.False(model.IsSleepTimerActive);
    }

    [Fact]
    public async Task AvailabilityTracksReadyBookAndDisposal_ButDoesNotRequireKnownDuration()
    {
        using var workspace = new TestWorkspace();
        var dialog = new TestDialog { Result = 20 };
        using var model = Create(workspace, dialog);
        var notifications = 0;
        model.StartCustomSleepTimerCommand.CanExecuteChanged += (_, _) => notifications++;
        model.IsFileLoaded = false;
        await AssertUnavailable();
        model.IsFileLoaded = true;
        model.IsBusy = true;
        await AssertUnavailable();
        model.IsBusy = false;
        model.DurationSeconds = 0;
        Assert.True(model.CanStartCustomSleepTimer);
        await model.StartCustomSleepTimerCommand.ExecuteAsync(null);
        Assert.True(model.IsSleepTimerActive);
        Assert.Equal(1, dialog.ShowCount);
        model.CancelSleepTimerCommand.Execute(null);
        model.Dispose();
        await AssertUnavailable();
        Assert.True(notifications > 0);
        Assert.Equal(1, dialog.ShowCount);

        async Task AssertUnavailable()
        {
            Assert.False(model.CanStartCustomSleepTimer);
            Assert.False(model.StartCustomSleepTimerCommand.CanExecute(null));
            await model.StartCustomSleepTimerCommand.ExecuteAsync(null);
        }
    }

    [Fact]
    public async Task DialogFailureReportsError_AndPreservesExistingTimer()
    {
        using var workspace = new TestWorkspace();
        using var model = Create(workspace, new TestDialog { Fail = true });
        model.StartSleepTimer45Command.Execute(null);
        await model.StartCustomSleepTimerCommand.ExecuteAsync(null);
        Assert.True(model.IsSleepTimerActive);
        Assert.Equal(TimeSpan.FromMinutes(45), model.SleepTimerRemaining);
        Assert.Contains("could not be set", model.ErrorMessage);
    }

    [Fact]
    public async Task CustomTimerDoesNotRearmOnNewApplicationSession()
    {
        using var workspace = new TestWorkspace();
        using (var model = Create(workspace, new TestDialog { Result = 37 }))
        {
            await model.StartCustomSleepTimerCommand.ExecuteAsync(null);
            Assert.True(model.IsSleepTimerActive);
        }
        using var reopened = Create(workspace, new TestDialog());
        Assert.False(reopened.IsSleepTimerActive);
        Assert.Equal(TimeSpan.Zero, reopened.SleepTimerRemaining);
        Assert.Equal("30", new CustomSleepTimerViewModel().MinutesText); // Remember/reuse is the next increment.
    }

    private static MainWindowViewModel Create(TestWorkspace workspace, ISleepTimerDurationService dialog)
    {
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        var model = new MainWindowViewModel(
            new TestEngine(), filePickerService: null!, progressStore: null!, bookmarkStore: null!,
            appSettingsStore: new SqliteAppSettingsStore(database), themeService: new NoOpThemeService(),
            audiobookLibrary: new SqliteAudiobookLibrary(database, workspace.ManagedLibraryPath),
            bookMetadataEditorService: null!, bookmarkEditorService: null!, bookRemovalConfirmationService: null!,
            managedLibraryIntegrityChecker: null!, managedLibraryMaintenance: null!, libraryBackupService: null!,
            managedFileVerifier: null!, managedFileRepairer: null!, jumpToTimeService: null!, sleepTimerDurationService: dialog);
        model.Chapters.Add(new(0, "Chapter 1", TimeSpan.Zero, TimeSpan.FromMinutes(30)));
        model.DurationSeconds = 1800;
        model.PositionSeconds = 300;
        model.IsFileLoaded = true;
        return model;
    }

    private sealed class TestDialog : ISleepTimerDurationService
    {
        public int? Result { get; init; }
        public Task<int?>? PendingResult { get; init; }
        public bool Fail { get; init; }
        public int ShowCount { get; private set; }
        public Task<int?> ChooseMinutesAsync()
        {
            ShowCount++;
            if (Fail) throw new InvalidOperationException("Test dialog failure");
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
        public TimeSpan Position => TimeSpan.FromSeconds(300);
        public TimeSpan Duration => TimeSpan.FromMinutes(30);
        public int Volume { get; set; }
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
        public bool TrySelectChapter(int chapterIndex) => true;
        public void Dispose() { }
    }
}
