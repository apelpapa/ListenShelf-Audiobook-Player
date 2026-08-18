using ListenShelf.Application.Playback;
using ListenShelf.Application.Settings;
using ListenShelf.Desktop.Services;
using ListenShelf.Desktop.ViewModels;
using ListenShelf.Infrastructure.Library;
using ListenShelf.Infrastructure.Settings;
using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Tests;

public sealed class ListeningTimeRemainingTests
{
    [Theory]
    [InlineData(0.75, "8:00:00")]
    [InlineData(1, "6:00:00")]
    [InlineData(1.25, "4:48:00")]
    [InlineData(1.5, "4:00:00")]
    [InlineData(1.75, "3:25:43")]
    [InlineData(2, "3:00:00")]
    public void SpeedChangesOnlyTheListeningEstimate_NotTheBookTimeline(double rate, string expectedTime)
    {
        using var workspace = new TestWorkspace();
        using var model = Create(workspace);
        model.DurationSeconds = 10 * 3600;
        model.PositionSeconds = 4 * 3600;
        model.IsFileLoaded = true;
        Assert.False(model.IsPlaying); // Estimates also work before pressing Play.

        model.SelectedPlaybackRate = rate;

        Assert.Equal($"Listening time left: ~{expectedTime} at {rate:0.##}×", model.ListeningTimeRemainingText);
        Assert.Equal("4:00:00", model.ElapsedText);
        Assert.Equal("10:00:00", model.DurationText);
        Assert.Equal("-6:00:00", model.RemainingText);
        Assert.Equal(4 * 3600, model.PositionSeconds);
        Assert.Equal(10 * 3600, model.SeekMaximum);
    }

    [Theory]
    [InlineData(360000, 0, 0.75, "133:20:00")]
    [InlineData(2700, 0, 0.75, "1:00:00")]
    [InlineData(7, 6.9, 2, "0:01")]
    [InlineData(60, -2, 1, "1:00")]
    [InlineData(60, 60, 1.5, "0:00")]
    [InlineData(60, 65, 1.5, "0:00")]
    public void Boundaries_RoundUpFractionalSecondsAndNeverWrapLongBooks(double duration, double position, double rate, string expectedTime)
    {
        using var workspace = new TestWorkspace();
        using var model = Create(workspace);
        model.DurationSeconds = duration;
        model.PositionSeconds = position;
        model.SelectedPlaybackRate = rate;

        Assert.Equal($"Listening time left: ~{expectedTime} at {rate:0.##}×", model.ListeningTimeRemainingText);
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(-1, 0, 1)]
    [InlineData(double.NaN, 0, 1)]
    [InlineData(double.PositiveInfinity, 0, 1)]
    [InlineData(60, double.NaN, 1)]
    [InlineData(60, 0, 0)]
    [InlineData(60, 0, double.NaN)]
    [InlineData(double.MaxValue, 0, 0.75)]
    public void UnknownOrInvalidInputs_ShowNoMisleadingEstimate(double duration, double position, double rate)
    {
        using var workspace = new TestWorkspace();
        using var model = Create(workspace);
        model.DurationSeconds = duration;
        model.PositionSeconds = position;
        model.SelectedPlaybackRate = rate;

        Assert.Equal("Listening time left: —", model.ListeningTimeRemainingText);
    }

    [Fact]
    public void DurationSeekAndSpeedChanges_NotifyTheBindingImmediately()
    {
        using var workspace = new TestWorkspace();
        using var model = Create(workspace);
        var notifications = new List<string?>();
        model.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);

        model.DurationSeconds = 3600;
        Assert.Contains(nameof(model.ListeningTimeRemainingText), notifications);
        Assert.Equal("Listening time left: ~1:00:00 at 1×", model.ListeningTimeRemainingText);

        notifications.Clear();
        model.IsFileLoaded = true;
        model.PositionSeconds = 600; // Same property used by the seek slider.
        Assert.Contains(nameof(model.ListeningTimeRemainingText), notifications);
        Assert.Equal("Listening time left: ~50:00 at 1×", model.ListeningTimeRemainingText);

        notifications.Clear();
        model.SelectedPlaybackRate = 2;
        Assert.Contains(nameof(model.ListeningTimeRemainingText), notifications);
        Assert.Equal("Listening time left: ~25:00 at 2×", model.ListeningTimeRemainingText);
    }

    [Fact]
    public void RememberedSpeed_IsUsedBeforePlaybackStarts()
    {
        using var workspace = new TestWorkspace();
        new SqliteAppSettingsStore(new ListenShelfDatabase(workspace.DatabasePath)).SavePlaybackRate(2);
        using var model = Create(workspace);
        model.DurationSeconds = 7200;
        model.PositionSeconds = 3600;
        model.IsFileLoaded = true;

        Assert.False(model.IsPlaying);
        Assert.Equal(2, model.SelectedPlaybackRate);
        Assert.Equal("Listening time left: ~30:00 at 2×", model.ListeningTimeRemainingText);
    }

    [Theory]
    [InlineData(0.75, "16:00")]
    [InlineData(1, "12:00")]
    [InlineData(1.5, "8:00")]
    [InlineData(2, "6:00")]
    public void CurrentChapterEstimate_UsesItsEndAndTheSelectedSpeed(double rate, string expected)
    {
        using var workspace = new TestWorkspace();
        using var model = Create(workspace);
        model.Chapters.Add(Chapter(0, 0, 600));
        model.Chapters.Add(Chapter(1, 600, 900));
        model.Chapters.Add(Chapter(2, 1500, 2100));
        model.DurationSeconds = 3600;
        model.PositionSeconds = 780;
        model.SelectedPlaybackRate = rate;
        model.IsFileLoaded = true;

        Assert.True(model.HasChapterListeningTimeEstimate);
        Assert.Equal($"Chapter time left: ~{expected} at {rate:0.##}×", model.ChapterListeningTimeRemainingText);
        Assert.False(model.IsPlaying);
        Assert.Equal("0:13:00", model.ElapsedText);
        Assert.Equal("-0:47:00", model.RemainingText);
    }

    [Theory]
    [InlineData(600, "10:00")]
    [InlineData(900, "5:00")]
    [InlineData(1199.9, "0:01")]
    [InlineData(1200, "10:00")]
    [InlineData(1800, "0:00")]
    [InlineData(1801, "0:00")]
    public void ChapterSeekAndBoundaryTransitions_WorkWithoutWaitingForEngineSelection(double position, string expected)
    {
        using var workspace = new TestWorkspace();
        using var model = Create(workspace);
        model.Chapters.Add(Chapter(0, 0, 600));
        model.Chapters.Add(Chapter(1, 600, 600));
        model.Chapters.Add(Chapter(2, 1200, 600));
        model.DurationSeconds = 1800;
        model.SelectedChapter = model.Chapters[0];
        model.IsFileLoaded = true;
        model.PositionSeconds = position;

        Assert.Equal(0, model.SelectedChapter.Index); // Simulate a delayed engine chapter event.
        Assert.Equal($"Chapter time left: ~{expected} at 1×", model.ChapterListeningTimeRemainingText);
        model.PositionSeconds = 300; // Rewind across the boundary again.
        Assert.Equal("Chapter time left: ~5:00 at 1×", model.ChapterListeningTimeRemainingText);
    }

    [Theory]
    [InlineData(0, 0, "10:00")]
    [InlineData(600, 0, null)]
    [InlineData(600, 1800, "20:00")]
    public void MissingChapterDurations_UseOnlyKnownNextOrBookBoundaries(double position, double bookDuration, string? expected)
    {
        using var workspace = new TestWorkspace();
        using var model = Create(workspace);
        model.Chapters.Add(Chapter(0, 0, 0));
        model.Chapters.Add(Chapter(1, 600, 0));
        model.PositionSeconds = position;
        model.DurationSeconds = bookDuration;
        model.IsFileLoaded = true;

        Assert.Equal(expected is not null, model.HasChapterListeningTimeEstimate);
        Assert.Equal(expected is null ? string.Empty : $"Chapter time left: ~{expected} at 1×", model.ChapterListeningTimeRemainingText);
    }

    [Fact]
    public void NoChaptersUnloadedBooksAndUnclearBoundaries_HideTheCountdown()
    {
        using var workspace = new TestWorkspace();
        using var model = Create(workspace);
        model.IsFileLoaded = true;
        AssertHidden();
        model.Chapters.Add(Chapter(0, 0, 600));
        Assert.True(model.HasChapterListeningTimeEstimate);
        model.IsFileLoaded = false;
        AssertHidden();
        model.IsFileLoaded = true;
        model.Chapters.Add(Chapter(1, 0, 600)); // Default/duplicate offsets are ambiguous.
        AssertHidden();
        model.Chapters[1] = Chapter(1, -1, 600);
        AssertHidden();
        model.Chapters.Clear();
        model.Chapters.Add(Chapter(0, 600, 600)); // Playhead is before the first chapter.
        AssertHidden();
        model.Chapters.Clear();
        model.Chapters.Add(Chapter(0, 0, 100));
        model.Chapters.Add(Chapter(1, 600, 600));
        model.PositionSeconds = 300; // A gap is not remaining time in the previous chapter.
        AssertHidden();

        void AssertHidden()
        {
            Assert.False(model.HasChapterListeningTimeEstimate);
            Assert.Empty(model.ChapterListeningTimeRemainingText);
        }
    }

    [Fact]
    public void ChapterCountdownClampsToNextChapterAndBookEnd_WithoutWrappingLongDurations()
    {
        using var workspace = new TestWorkspace();
        using var model = Create(workspace);
        model.IsFileLoaded = true;
        model.Chapters.Add(Chapter(0, 0, 2000));
        model.Chapters.Add(Chapter(1, 600, 2000));
        model.DurationSeconds = 1200;
        Assert.Equal("Chapter time left: ~10:00 at 1×", model.ChapterListeningTimeRemainingText);
        model.PositionSeconds = 900;
        Assert.Equal("Chapter time left: ~5:00 at 1×", model.ChapterListeningTimeRemainingText);
        model.Chapters.Clear();
        model.Chapters.Add(Chapter(0, 0, 360000));
        model.DurationSeconds = 360000;
        model.PositionSeconds = 0;
        model.SelectedPlaybackRate = 0.75;
        Assert.Equal($"Chapter time left: ~133:20:00 at {0.75:0.##}×", model.ChapterListeningTimeRemainingText);
    }

    [Fact]
    public void ChapterTimingInputs_NotifyVisibilityAndTextBindings()
    {
        using var workspace = new TestWorkspace();
        using var model = Create(workspace);
        var notifications = new List<string?>();
        model.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        Change(() => model.Chapters.Add(Chapter(0, 0, 0)));
        Change(() => model.IsFileLoaded = true);
        Assert.False(model.HasChapterListeningTimeEstimate);
        Change(() => model.DurationSeconds = 1200);
        Assert.True(model.HasChapterListeningTimeEstimate);
        Change(() => model.PositionSeconds = 600);
        Change(() => model.SelectedPlaybackRate = 2);
        Assert.Equal("Chapter time left: ~5:00 at 2×", model.ChapterListeningTimeRemainingText);
        Change(() => model.SelectedChapter = model.Chapters[0]);
        Change(() => model.Chapters.Clear());
        Assert.False(model.HasChapterListeningTimeEstimate);

        void Change(Action change)
        {
            notifications.Clear();
            change();
            Assert.Contains(nameof(model.HasChapterListeningTimeEstimate), notifications);
            Assert.Contains(nameof(model.ChapterListeningTimeRemainingText), notifications);
        }
    }

    private static PlaybackChapterItemViewModel Chapter(int index, double startSeconds, double durationSeconds) =>
        new(index, $"Chapter {index + 1}", TimeSpan.FromSeconds(startSeconds), TimeSpan.FromSeconds(durationSeconds));

    private static MainWindowViewModel Create(TestWorkspace workspace)
    {
        var database = new ListenShelfDatabase(workspace.DatabasePath);
        // These presentation tests do not open media, dialogs, or a real library.
        return new MainWindowViewModel(
            audioEngine: new IdleAudioEngine(), filePickerService: null!, progressStore: null!, bookmarkStore: null!,
            appSettingsStore: new SqliteAppSettingsStore(database), themeService: new NoOpThemeService(),
            audiobookLibrary: new SqliteAudiobookLibrary(database, workspace.ManagedLibraryPath),
            bookMetadataEditorService: null!, bookmarkEditorService: null!, bookRemovalConfirmationService: null!,
            managedLibraryIntegrityChecker: null!, managedLibraryMaintenance: null!, libraryBackupService: null!,
            managedFileVerifier: null!, managedFileRepairer: null!);
    }

    private sealed class NoOpThemeService : IThemeService
    {
        public void ApplyTheme(AppTheme theme) { }
    }

    private sealed class IdleAudioEngine : IAudioEngine
    {
        public event EventHandler<PlaybackProgressChangedEventArgs>? ProgressChanged { add { } remove { } }
        public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged { add { } remove { } }
        public event EventHandler<PlaybackChaptersChangedEventArgs>? ChaptersChanged { add { } remove { } }
        public string? CurrentFilePath => null;
        public TimeSpan Position => TimeSpan.Zero;
        public TimeSpan Duration => TimeSpan.FromHours(10);
        public int Volume { get; set; }
        public double PlaybackRate { get; private set; } = 1;
        public IReadOnlyList<AudioChapter> Chapters => [];
        public int CurrentChapterIndex => -1;
        public Task LoadAsync(string filePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Unload() => throw new NotSupportedException();
        public bool Play() => throw new NotSupportedException();
        public void Pause() => throw new NotSupportedException();
        public void Stop() => throw new NotSupportedException();
        public void Seek(TimeSpan position) { }
        public bool TrySetPlaybackRate(double rate) { PlaybackRate = rate; return true; }
        public bool TrySelectChapter(int chapterIndex) => true;
        public void Dispose() { }
    }
}
