using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ListenShelf.Application.Bookmarks;
using ListenShelf.Application.Library;
using ListenShelf.Application.Playback;
using ListenShelf.Application.Progress;
using ListenShelf.Application.Settings;
using ListenShelf.Desktop.Services;

namespace ListenShelf.Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan AutomaticSaveInterval = TimeSpan.FromSeconds(10);
    private const int MaximumDisplayedStorageIssues = 50;
    private const double DefaultLibraryTileWidth = 220d;
    private const double MinimumLibraryTileWidth = 180d;
    private const double MaximumLibraryTileWidth = 320d;
    private const double DefaultPlaybackVolume = 80d;
    private const double MinimumPlaybackVolume = 0d;
    private const double MaximumPlaybackVolume = 100d;
    private const double DefaultPlaybackRate = 1d;
    private static readonly LibraryGroupOptionViewModel[] GroupOptions =
    [
        new(LibraryGroupMode.None, "None"),
        new(LibraryGroupMode.Series, "Series"),
        new(LibraryGroupMode.Author, "Author"),
        new(LibraryGroupMode.Narrator, "Narrator"),
        new(LibraryGroupMode.Genre, "Genre"),
        new(LibraryGroupMode.Publisher, "Publisher"),
        new(LibraryGroupMode.Year, "Year"),
    ];

    private readonly IAudioEngine _audioEngine;
    private readonly IFilePickerService _filePickerService;
    private readonly IPlaybackProgressStore _progressStore;
    private readonly IPlaybackBookmarkStore _bookmarkStore;
    private readonly IAppSettingsStore _appSettingsStore;
    private readonly IThemeService _themeService;
    private readonly IAudiobookLibrary _audiobookLibrary;
    private readonly IBookMetadataEditorService _bookMetadataEditorService;
    private readonly IBookmarkEditorService _bookmarkEditorService;
    private readonly IBookRemovalConfirmationService _bookRemovalConfirmationService;
    private readonly IManagedLibraryIntegrityChecker _managedLibraryIntegrityChecker;
    private readonly IManagedLibraryMaintenance _managedLibraryMaintenance;
    private readonly DispatcherTimer _sleepTimer;
    private bool _isUpdatingPositionFromEngine;
    private bool _isUpdatingChapterFromEngine;
    private bool _isLoadingFile;
    private bool _hasPlaybackEnded;
    private bool _sleepTimerPausePending;
    private string? _currentFilePath;
    private TimeSpan? _pendingResumePosition;
    private DateTimeOffset? _sleepTimerDeadlineUtc;
    private DateTimeOffset _lastSavedAtUtc = DateTimeOffset.MinValue;
    private Bitmap? _currentCoverImage;
    private bool _initializationStarted;
    private bool _disposed;

    public MainWindowViewModel(
        IAudioEngine audioEngine,
        IFilePickerService filePickerService,
        IPlaybackProgressStore progressStore,
        IPlaybackBookmarkStore bookmarkStore,
        IAppSettingsStore appSettingsStore,
        IThemeService themeService,
        IAudiobookLibrary audiobookLibrary,
        IBookMetadataEditorService bookMetadataEditorService,
        IBookmarkEditorService bookmarkEditorService,
        IBookRemovalConfirmationService bookRemovalConfirmationService,
        IManagedLibraryIntegrityChecker managedLibraryIntegrityChecker,
        IManagedLibraryMaintenance managedLibraryMaintenance)
    {
        _audioEngine = audioEngine;
        _filePickerService = filePickerService;
        _progressStore = progressStore;
        _bookmarkStore = bookmarkStore;
        _appSettingsStore = appSettingsStore;
        _themeService = themeService;
        _audiobookLibrary = audiobookLibrary;
        _bookMetadataEditorService = bookMetadataEditorService;
        _bookmarkEditorService = bookmarkEditorService;
        _bookRemovalConfirmationService = bookRemovalConfirmationService;
        _managedLibraryIntegrityChecker = managedLibraryIntegrityChecker;
        _managedLibraryMaintenance = managedLibraryMaintenance;
        _sleepTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _sleepTimer.Tick += OnSleepTimerTick;

        try
        {
            _selectedTheme = _appSettingsStore.GetTheme();
            _appearanceSettingsMessage = $"{_selectedTheme} appearance is active.";
        }
        catch (Exception exception)
        {
            _selectedTheme = AppTheme.Dark;
            _appearanceSettingsMessage =
                $"Appearance preference could not be loaded: {exception.Message}";
        }

        try
        {
            _selectedLibraryView = _appSettingsStore.GetLibraryViewMode();
        }
        catch
        {
            _selectedLibraryView = LibraryViewMode.List;
        }

        try
        {
            var savedGroupMode = _appSettingsStore.GetLibraryGroupMode();
            _selectedLibraryGroupOption = GroupOptions.First(option =>
                option.Mode == savedGroupMode);
        }
        catch
        {
            _selectedLibraryGroupOption = GroupOptions[0];
        }

        try
        {
            _libraryTileWidth = Math.Clamp(
                _appSettingsStore.GetLibraryTileWidth(),
                MinimumLibraryTileWidth,
                MaximumLibraryTileWidth);
        }
        catch
        {
            _libraryTileWidth = DefaultLibraryTileWidth;
        }

        try
        {
            var savedVolume = _appSettingsStore.GetPlaybackVolume();
            _volume = double.IsFinite(savedVolume)
                ? Math.Clamp(
                    savedVolume,
                    MinimumPlaybackVolume,
                    MaximumPlaybackVolume)
                : DefaultPlaybackVolume;
        }
        catch
        {
            _volume = DefaultPlaybackVolume;
        }

        try
        {
            var savedPlaybackRate = _appSettingsStore.GetPlaybackRate();
            _selectedPlaybackRate = PlaybackRates.Contains(savedPlaybackRate)
                ? savedPlaybackRate
                : DefaultPlaybackRate;
        }
        catch
        {
            _selectedPlaybackRate = DefaultPlaybackRate;
        }

        _themeService.ApplyTheme(_selectedTheme);

        _audioEngine.ProgressChanged += OnProgressChanged;
        _audioEngine.StateChanged += OnStateChanged;
        _audioEngine.ChaptersChanged += OnChaptersChanged;
        _audioEngine.Volume = (int)Volume;
        _audioEngine.TrySetPlaybackRate(SelectedPlaybackRate);

        RefreshLibrary();
    }

    public IReadOnlyList<double> PlaybackRates { get; } =
        [0.75d, 1d, 1.25d, 1.5d, 1.75d, 2d];

    public async Task InitializeAsync()
    {
        if (_initializationStarted)
        {
            return;
        }

        _initializationStarted = true;
        PlaybackProgress? mostRecentProgress;
        try
        {
            mostRecentProgress = _progressStore.GetMostRecent();
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Your last audiobook could not be restored: {exception.Message}";
            return;
        }

        if (mostRecentProgress is null)
        {
            return;
        }

        LibraryBook? libraryBook = null;
        try
        {
            libraryBook = _audiobookLibrary
                .GetBooks()
                .FirstOrDefault(book => PathsEqual(book.FilePath, mostRecentProgress.FilePath));
        }
        catch
        {
            return;
        }

        if (libraryBook is null)
        {
            return;
        }

        if (!File.Exists(libraryBook.FilePath))
        {
            StatusText = "Last audiobook unavailable";
            ErrorMessage =
                $"The last audiobook could not be found at {libraryBook.FilePath}";
            return;
        }

        if (await LoadFileAsync(
                libraryBook,
                autoPlay: false,
                knownProgress: mostRecentProgress))
        {
            SelectedSection = AppSection.Player;
        }
    }

    public ObservableCollection<LibraryBookItemViewModel> LibraryBooks { get; } = [];

    public ObservableCollection<PlaybackChapterItemViewModel> Chapters { get; } = [];

    public ObservableCollection<PlaybackBookmarkItemViewModel> Bookmarks { get; } = [];

    public ObservableCollection<ManagedStorageIssueItemViewModel> ManagedStorageIssues { get; } = [];

    public ObservableCollection<LibraryGroupViewModel> LibraryGroups { get; } = [];

    public IReadOnlyList<LibraryGroupOptionViewModel> LibraryGroupOptions => GroupOptions;

    public double MinimumTileWidth => MinimumLibraryTileWidth;

    public double MaximumTileWidth => MaximumLibraryTileWidth;

    public string LibraryTileSizeText => $"{LibraryTileWidth:0} px";

    public bool IsLibraryGroupingActive =>
        SelectedLibraryGroupOption.Mode != LibraryGroupMode.None;

    public bool IsLibraryGroupOverviewVisible =>
        IsLibraryGroupingActive && ActiveLibraryGroup is null;

    public bool IsLibraryGroupDetailVisible =>
        IsLibraryGroupingActive && ActiveLibraryGroup is not null;

    public bool IsUngroupedLibraryVisible => !IsLibraryGroupingActive;

    public string ActiveLibraryGroupName => ActiveLibraryGroup?.Name ?? string.Empty;

    public string ActiveLibraryGroupCountText => ActiveLibraryGroup?.CountText ?? string.Empty;

    public IReadOnlyList<LibraryBookItemViewModel> ActiveLibraryGroupBooks =>
        ActiveLibraryGroup?.Books ?? [];

    public string WindowTitle => "ListenShelf — Audiobook Player";

    public string FooterText =>
        "Offline playback • Imports are copied and verified • Originals stay untouched";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibrarySection))]
    [NotifyPropertyChangedFor(nameof(IsPlayerSection))]
    [NotifyPropertyChangedFor(nameof(IsStorageCareSection))]
    [NotifyPropertyChangedFor(nameof(IsSettingsSection))]
    [NotifyPropertyChangedFor(nameof(PageTitle))]
    [NotifyPropertyChangedFor(nameof(PageSubtitle))]
    private AppSection _selectedSection = AppSection.Library;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDarkThemeSelected))]
    [NotifyPropertyChangedFor(nameof(IsLightThemeSelected))]
    private AppTheme _selectedTheme = AppTheme.Dark;

    [ObservableProperty]
    private string _appearanceSettingsMessage = "Dark appearance is active.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibraryListView))]
    [NotifyPropertyChangedFor(nameof(IsLibraryTileView))]
    private LibraryViewMode _selectedLibraryView = LibraryViewMode.List;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibraryGroupingActive))]
    [NotifyPropertyChangedFor(nameof(IsLibraryGroupOverviewVisible))]
    [NotifyPropertyChangedFor(nameof(IsLibraryGroupDetailVisible))]
    [NotifyPropertyChangedFor(nameof(IsUngroupedLibraryVisible))]
    private LibraryGroupOptionViewModel _selectedLibraryGroupOption = GroupOptions[0];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibraryGroupOverviewVisible))]
    [NotifyPropertyChangedFor(nameof(IsLibraryGroupDetailVisible))]
    [NotifyPropertyChangedFor(nameof(ActiveLibraryGroupName))]
    [NotifyPropertyChangedFor(nameof(ActiveLibraryGroupCountText))]
    [NotifyPropertyChangedFor(nameof(ActiveLibraryGroupBooks))]
    private LibraryGroupViewModel? _activeLibraryGroup;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LibraryTileSizeText))]
    private double _libraryTileWidth = DefaultLibraryTileWidth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddAudiobooks))]
    [NotifyCanExecuteChangedFor(nameof(CheckManagedStorageCommand))]
    private bool _isLibraryBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCheckManagedStorage))]
    [NotifyCanExecuteChangedFor(nameof(CheckManagedStorageCommand))]
    private bool _isManagedStorageCheckRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsManagedStorageHealthy))]
    private bool _hasManagedStorageBeenChecked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasManagedStorageIssues))]
    [NotifyPropertyChangedFor(nameof(IsManagedStorageHealthy))]
    [NotifyPropertyChangedFor(nameof(ManagedStorageAttentionText))]
    private int _managedStorageIssueCount;

    [ObservableProperty]
    private string _managedStorageStatusText =
        "ListenShelf will check that every managed audiobook file and folder is accounted for.";

    [ObservableProperty]
    private string _managedStorageLastCheckedText = "Not checked yet.";

    [ObservableProperty]
    private string _libraryStatusMessage = "Add M4B, M4A, or MP3 audiobooks to begin building your shelf.";

    [ObservableProperty]
    private string _bookTitle = "No audiobook selected";

    [ObservableProperty]
    private string _fileName = "Open an audiobook to begin listening.";

    [ObservableProperty]
    private string _fileFormatText = "AUDIO • LOCAL";

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _progressText = "Your place will be saved automatically.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanControlPlayback))]
    [NotifyPropertyChangedFor(nameof(CanCreateBookmark))]
    [NotifyPropertyChangedFor(nameof(CanDisplayBookmarkPanel))]
    [NotifyCanExecuteChangedFor(nameof(PreviousChapterCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextChapterCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddBookmarkCommand))]
    private bool _isFileLoaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayPauseLabel))]
    private bool _isPlaying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanControlPlayback))]
    [NotifyPropertyChangedFor(nameof(CanCreateBookmark))]
    [NotifyCanExecuteChangedFor(nameof(PreviousChapterCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextChapterCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddBookmarkCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChapterPositionText))]
    [NotifyCanExecuteChangedFor(nameof(PreviousChapterCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextChapterCommand))]
    private PlaybackChapterItemViewModel? _selectedChapter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ElapsedText))]
    [NotifyPropertyChangedFor(nameof(RemainingText))]
    private double _positionSeconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationText))]
    [NotifyPropertyChangedFor(nameof(RemainingText))]
    [NotifyPropertyChangedFor(nameof(SeekMaximum))]
    private double _durationSeconds;

    [ObservableProperty]
    private double _volume = 80d;

    [ObservableProperty]
    private double _selectedPlaybackRate = 1d;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SleepTimerButtonText))]
    [NotifyPropertyChangedFor(nameof(SleepTimerStatusText))]
    private bool _isSleepTimerActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SleepTimerButtonText))]
    [NotifyPropertyChangedFor(nameof(SleepTimerStatusText))]
    private TimeSpan _sleepTimerRemaining;

    public Bitmap? CurrentCoverImage => _currentCoverImage;

    public bool HasCurrentCover => CurrentCoverImage is not null;

    public bool HasNoCurrentCover => !HasCurrentCover;

    public bool CanControlPlayback => IsFileLoaded && !IsBusy;

    public bool CanCreateBookmark => CanControlPlayback;

    public bool CanDisplayBookmarkPanel => IsFileLoaded;

    public bool HasChapters => Chapters.Count > 0;

    public bool HasBookmarks => Bookmarks.Count > 0;

    public bool HasNoBookmarks => !HasBookmarks;

    public string BookmarkCountText => Bookmarks.Count == 1
        ? "1 bookmark"
        : $"{Bookmarks.Count} bookmarks";

    public string SleepTimerButtonText => IsSleepTimerActive
        ? $"Sleep {FormatSleepTimerRemaining(SleepTimerRemaining)}"
        : "Sleep timer";

    public string SleepTimerStatusText => IsSleepTimerActive
        ? $"Playback pauses in {FormatSleepTimerRemaining(SleepTimerRemaining)}."
        : "Choose when playback should pause.";

    public string ChapterPositionText => SelectedChapter is null
        ? string.Empty
        : $"Chapter {SelectedChapter.Index + 1} of {Chapters.Count}";

    public bool TryHandlePlaybackControl(PlaybackControlAction action)
    {
        if (!CanControlPlayback)
        {
            return false;
        }

        switch (action)
        {
            case PlaybackControlAction.TogglePlayPause:
                TogglePlayback();
                break;
            case PlaybackControlAction.Play:
                if (!IsPlaying)
                {
                    TogglePlayback();
                }

                break;
            case PlaybackControlAction.Pause:
                if (IsPlaying)
                {
                    _audioEngine.Pause();
                }

                break;
            case PlaybackControlAction.SkipBackward:
                SkipBackward();
                break;
            case PlaybackControlAction.SkipForward:
                SkipForward();
                break;
            default:
                return false;
        }

        return true;
    }

    public bool IsLibrarySection => SelectedSection == AppSection.Library;

    public bool IsPlayerSection => SelectedSection == AppSection.Player;

    public bool IsStorageCareSection => SelectedSection == AppSection.StorageCare;

    public bool IsSettingsSection => SelectedSection == AppSection.Settings;

    public bool IsDarkThemeSelected => SelectedTheme == AppTheme.Dark;

    public bool IsLightThemeSelected => SelectedTheme == AppTheme.Light;

    public bool IsLibraryListView => SelectedLibraryView == LibraryViewMode.List;

    public bool IsLibraryTileView => SelectedLibraryView == LibraryViewMode.Tiles;

    public bool HasLibraryBooks => LibraryBooks.Count > 0;

    public bool IsLibraryEmpty => !HasLibraryBooks;

    public bool CanAddAudiobooks => !IsLibraryBusy;

    public bool CanCheckManagedStorage => !IsLibraryBusy && !IsManagedStorageCheckRunning;

    public bool HasManagedStorageIssues => ManagedStorageIssueCount > 0;

    public bool IsManagedStorageHealthy =>
        HasManagedStorageBeenChecked && !HasManagedStorageIssues;

    public string ManagedStorageAttentionText => ManagedStorageIssueCount == 1
        ? "1 storage item needs attention"
        : $"{ManagedStorageIssueCount} storage items need attention";

    public string LibraryBookCountText => LibraryBooks.Count == 1
        ? "1 audiobook"
        : $"{LibraryBooks.Count} audiobooks";

    public string ManagedLibraryPath => _audiobookLibrary.ManagedLibraryPath;

    public string PageTitle => SelectedSection switch
    {
        AppSection.Player => "Player",
        AppSection.StorageCare => "Storage care",
        AppSection.Settings => "Settings",
        _ => "Library",
    };

    public string PageSubtitle => SelectedSection switch
    {
        AppSection.Player => "Listen locally with automatic progress saving.",
        AppSection.StorageCare => "Recover useful orphaned audiobooks or clean up unneeded storage.",
        AppSection.Settings => "Personalize ListenShelf.",
        _ => "Your audiobooks, series, and collections will live here.",
    };

    public string LibraryEmptyDescription =>
        "Choose M4B, M4A, or MP3 files. ListenShelf will make verified copies and leave the originals untouched.";

    public string PlayPauseLabel => IsPlaying ? "Pause" : "Play";

    public double SeekMaximum => Math.Max(1d, DurationSeconds);

    public string ElapsedText => FormatTime(PositionSeconds, DurationSeconds);

    public string DurationText => FormatTime(DurationSeconds, DurationSeconds);

    public string RemainingText =>
        $"-{FormatTime(Math.Max(0d, DurationSeconds - PositionSeconds), DurationSeconds)}";

    [RelayCommand]
    private void ShowLibrary()
    {
        RefreshLibrary();
        SelectedSection = AppSection.Library;
    }

    [RelayCommand]
    private void ShowPlayer() => SelectedSection = AppSection.Player;

    [RelayCommand]
    private void ShowStorageCare() => SelectedSection = AppSection.StorageCare;

    [RelayCommand]
    private void ShowSettings() => SelectedSection = AppSection.Settings;

    [RelayCommand(CanExecute = nameof(CanCheckManagedStorage))]
    public async Task CheckManagedStorageAsync()
    {
        if (!CanCheckManagedStorage)
        {
            return;
        }

        IsManagedStorageCheckRunning = true;
        ManagedStorageStatusText = "Checking managed library storage…";

        try
        {
            var report = await Task.Run(_managedLibraryIntegrityChecker.Check);
            ManagedStorageIssues.Clear();
            foreach (var issue in report.Issues.Take(MaximumDisplayedStorageIssues))
            {
                ManagedStorageIssues.Add(new ManagedStorageIssueItemViewModel(
                    issue,
                    RecoverManagedStorageIssueAsync,
                    CleanUpManagedStorageIssueAsync));
            }

            ManagedStorageIssueCount = report.Issues.Count;
            HasManagedStorageBeenChecked = true;
            ManagedStorageLastCheckedText =
                $"Last checked {report.CheckedAtUtc.ToLocalTime():g}.";

            if (report.IsHealthy)
            {
                ManagedStorageStatusText = report.CatalogBookCount == 1
                    ? "All managed storage is accounted for across 1 cataloged audiobook. No files were changed."
                    : $"All managed storage is accounted for across {report.CatalogBookCount} cataloged audiobooks. No files were changed.";
            }
            else
            {
                var displayNote = report.Issues.Count > MaximumDisplayedStorageIssues
                    ? $" Showing the first {MaximumDisplayedStorageIssues}."
                    : string.Empty;
                ManagedStorageStatusText =
                    $"Found {report.Issues.Count} managed-storage issue{(report.Issues.Count == 1 ? string.Empty : "s")}.{displayNote} No files were changed.";
            }
        }
        catch (Exception exception)
        {
            ManagedStorageIssues.Clear();
            ManagedStorageIssueCount = 0;
            HasManagedStorageBeenChecked = false;
            ManagedStorageLastCheckedText = "The most recent check did not finish.";
            ManagedStorageStatusText =
                $"ListenShelf could not check managed storage: {exception.Message}";
        }
        finally
        {
            IsManagedStorageCheckRunning = false;
        }
    }

    private async Task RecoverManagedStorageIssueAsync(ManagedStorageIssueItemViewModel item)
    {
        if (IsLibraryBusy || IsManagedStorageCheckRunning)
        {
            return;
        }

        IsLibraryBusy = true;
        ManagedStorageStatusText = $"Recovering {item.Name} into the library…";
        string outcome;

        try
        {
            var result = await Task.Run(() =>
                _managedLibraryMaintenance.RecoverAudiobook(item.Path));
            RefreshLibrary();
            outcome = result.OrphanCleanupPending
                ? $"{result.Book.Title} was recovered. Its old orphaned copy could not be removed and still needs attention."
                : $"{result.Book.Title} was recovered and is back in your library.";
        }
        catch (Exception exception)
        {
            outcome = $"The audiobook could not be recovered: {exception.Message}";
        }
        finally
        {
            IsLibraryBusy = false;
        }

        await CheckManagedStorageAsync();
        ManagedStorageStatusText = $"{outcome} {ManagedStorageStatusText}";
    }

    private async Task CleanUpManagedStorageIssueAsync(ManagedStorageIssueItemViewModel item)
    {
        if (IsLibraryBusy || IsManagedStorageCheckRunning)
        {
            return;
        }

        IsLibraryBusy = true;
        ManagedStorageStatusText = $"Cleaning up {item.Name}…";
        string outcome;

        try
        {
            var result = await Task.Run(() =>
                _managedLibraryMaintenance.CleanUp(item.Path));
            outcome = result.WasDirectory
                ? $"The confirmed orphaned folder {item.Name} was permanently deleted."
                : $"The confirmed orphaned file {item.Name} was permanently deleted.";
        }
        catch (Exception exception)
        {
            outcome = $"The orphaned item could not be cleaned up: {exception.Message}";
        }
        finally
        {
            IsLibraryBusy = false;
        }

        await CheckManagedStorageAsync();
        ManagedStorageStatusText = $"{outcome} {ManagedStorageStatusText}";
    }

    [RelayCommand]
    private void UseDarkTheme() => SaveTheme(AppTheme.Dark);

    [RelayCommand]
    private void UseLightTheme() => SaveTheme(AppTheme.Light);

    [RelayCommand]
    private void ShowLibraryAsList() => SaveLibraryViewMode(LibraryViewMode.List);

    [RelayCommand]
    private void ShowLibraryAsTiles() => SaveLibraryViewMode(LibraryViewMode.Tiles);

    partial void OnSelectedLibraryGroupOptionChanged(LibraryGroupOptionViewModel value)
    {
        ActiveLibraryGroup = null;
        RebuildLibraryGroups();

        try
        {
            _appSettingsStore.SaveLibraryGroupMode(value.Mode);
        }
        catch (Exception exception)
        {
            LibraryStatusMessage =
                $"Grouped by {value.DisplayName.ToLowerInvariant()} for this session, but the choice could not be remembered: {exception.Message}";
        }
    }

    [RelayCommand]
    private void ReturnToLibraryGroups() => ActiveLibraryGroup = null;

    partial void OnLibraryTileWidthChanged(double value)
    {
        foreach (var book in LibraryBooks)
        {
            book.SetTileWidth(value);
        }

        try
        {
            _appSettingsStore.SaveLibraryTileWidth(value);
        }
        catch (Exception exception)
        {
            LibraryStatusMessage =
                $"The tile size changed for this session, but could not be remembered: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task AddAudiobooksAsync()
    {
        if (!CanAddAudiobooks)
        {
            return;
        }

        try
        {
            var filePaths = await _filePickerService.PickAudiobookFilesAsync();
            if (filePaths.Count == 0)
            {
                return;
            }

            IsLibraryBusy = true;
            LibraryStatusMessage =
                $"Copying {filePaths.Count} audiobook(s) into the library…";

            var addedCount = 0;
            var existingCount = 0;
            var failures = new List<string>();

            foreach (var filePath in filePaths)
            {
                try
                {
                    var result = await Task.Run(() => _audiobookLibrary.Import(filePath));
                    if (result.WasAdded)
                    {
                        addedCount++;
                    }
                    else
                    {
                        existingCount++;
                    }
                }
                catch (Exception exception)
                {
                    failures.Add($"{Path.GetFileName(filePath)}: {exception.Message}");
                }
            }

            RefreshLibrary();
            LibraryStatusMessage = BuildImportSummary(addedCount, existingCount, failures);
        }
        catch (Exception exception)
        {
            LibraryStatusMessage = $"Audiobooks could not be selected: {exception.Message}";
        }
        finally
        {
            IsLibraryBusy = false;
        }
    }

    private async Task<bool> LoadFileAsync(
        LibraryBook libraryBook,
        bool autoPlay = true,
        PlaybackProgress? knownProgress = null)
    {
        var filePath = libraryBook.FilePath;

        try
        {
            ErrorMessage = string.Empty;

            IsBusy = true;
            StatusText = "Opening audiobook…";
            SaveCurrentProgress(force: true);
            _isLoadingFile = true;
            IsFileLoaded = false;
            _currentFilePath = null;
            _pendingResumePosition = null;
            ClearChapters();
            ClearBookmarks();
            SetCurrentCover(null);

            await _audioEngine.LoadAsync(filePath);

            _currentFilePath = Path.GetFullPath(filePath);
            var savedProgress = knownProgress ?? _progressStore.Get(_currentFilePath);
            _pendingResumePosition = savedProgress?.Position > TimeSpan.Zero
                ? savedProgress.Position
                : null;

            BookTitle = libraryBook.Title;
            FileName = Path.GetFileName(_currentFilePath);
            FileFormatText = $"{Path.GetExtension(_currentFilePath).TrimStart('.').ToUpperInvariant()} • LOCAL";
            SetCurrentCover(libraryBook.CoverPath);
            RefreshBookmarks();
            ProgressText = _pendingResumePosition is { } resumePosition
                ? autoPlay
                    ? $"Resuming from {FormatTime(resumePosition.TotalSeconds, savedProgress?.Duration.TotalSeconds ?? 0d)}"
                    : $"Ready at {FormatTime(resumePosition.TotalSeconds, savedProgress?.Duration.TotalSeconds ?? 0d)}"
                : autoPlay
                    ? "Starting from the beginning"
                    : "Ready at the beginning";
            IsFileLoaded = true;
            _lastSavedAtUtc = DateTimeOffset.UtcNow;

            if (autoPlay)
            {
                if (!_audioEngine.Play())
                {
                    _isLoadingFile = false;
                    ErrorMessage = "The audiobook could not be started.";
                    return false;
                }
            }
            else
            {
                await Dispatcher.UIThread.InvokeAsync(
                    () => ApplyRestoredProgress(savedProgress),
                    DispatcherPriority.Background);
                _isLoadingFile = false;
                StatusText = "Ready to play";
            }

            return true;
        }
        catch (Exception exception)
        {
            _isLoadingFile = false;
            ErrorMessage = exception.Message;
            StatusText = "Could not open audiobook";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PlayLibraryBookAsync(LibraryBook book)
    {
        if (!File.Exists(book.FilePath))
        {
            LibraryStatusMessage =
                $"{book.Title} is missing from its saved location. Re-import it to add the new location.";
            RefreshLibrary();
            return;
        }

        SelectedSection = AppSection.Player;
        await LoadFileAsync(book);
    }

    private async Task ChooseCoverAsync(LibraryBook book)
    {
        try
        {
            var imagePath = await _filePickerService.PickCoverImageAsync();
            if (imagePath is null)
            {
                return;
            }

            IsLibraryBusy = true;
            LibraryStatusMessage = $"Adding a cover for {book.Title}…";

            using (var image = new Bitmap(imagePath))
            {
                if (image.PixelSize.Width <= 0 || image.PixelSize.Height <= 0)
                {
                    throw new InvalidDataException("The selected file is not a readable image.");
                }
            }

            var updatedBook = await Task.Run(() => _audiobookLibrary.SetCover(book.Id, imagePath));
            if (!string.IsNullOrWhiteSpace(_currentFilePath)
                && PathsEqual(_currentFilePath, updatedBook.FilePath))
            {
                SetCurrentCover(updatedBook.CoverPath);
            }

            RefreshLibrary();
            LibraryStatusMessage = $"Cover saved for {updatedBook.Title}.";
        }
        catch (Exception exception)
        {
            LibraryStatusMessage = $"The cover could not be added: {exception.Message}";
        }
        finally
        {
            IsLibraryBusy = false;
        }
    }

    private async Task EditMetadataAsync(LibraryBook book)
    {
        try
        {
            var suggestions = AudiobookMetadataSuggestions.FromBooks(
                _audiobookLibrary.GetBooks());
            var editResult = await _bookMetadataEditorService.EditAsync(book, suggestions);
            if (editResult is null)
            {
                return;
            }

            IsLibraryBusy = true;
            LibraryStatusMessage = $"Saving details for {book.Title}…";

            var updatedBook = await Task.Run(() =>
                _audiobookLibrary.UpdateMetadata(book.Id, editResult.Metadata));
            Exception? coverError = null;
            if (editResult.CoverImage is { } coverImage)
            {
                try
                {
                    updatedBook = await Task.Run(() => _audiobookLibrary.SetCover(
                        book.Id,
                        coverImage.Bytes,
                        coverImage.FileExtension));
                }
                catch (Exception exception)
                {
                    coverError = exception;
                }
            }

            if (!string.IsNullOrWhiteSpace(_currentFilePath)
                && PathsEqual(_currentFilePath, updatedBook.FilePath))
            {
                BookTitle = updatedBook.Title;
                SetCurrentCover(updatedBook.CoverPath);
            }

            RefreshLibrary();
            LibraryStatusMessage = coverError is null
                ? $"Details saved for {updatedBook.Title}."
                : $"Details saved for {updatedBook.Title}, but its online cover could not be saved: {coverError.Message}";
        }
        catch (Exception exception)
        {
            LibraryStatusMessage = $"The audiobook details could not be saved: {exception.Message}";
        }
        finally
        {
            IsLibraryBusy = false;
        }
    }

    private async Task RemoveBookAsync(LibraryBook book)
    {
        if (IsLibraryBusy)
        {
            return;
        }

        IsLibraryBusy = true;
        try
        {
            if (!await _bookRemovalConfirmationService.ConfirmRemovalAsync(book))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(_currentFilePath)
                && PathsEqual(_currentFilePath, book.FilePath))
            {
                UnloadCurrentBookForRemoval();
            }

            var result = await Task.Run(() => _audiobookLibrary.Remove(book.Id));
            RefreshLibrary();
            LibraryStatusMessage = result.CleanupPending
                ? $"{result.Title} was removed. ListenShelf will retry leftover file cleanup next launch."
                : $"{result.Title} and its ListenShelf-managed data were permanently removed.";
        }
        catch (Exception exception)
        {
            RefreshLibrary();
            LibraryStatusMessage = $"The audiobook could not be removed: {exception.Message}";
        }
        finally
        {
            IsLibraryBusy = false;
        }
    }

    [RelayCommand]
    private void TogglePlayback()
    {
        if (!CanControlPlayback)
        {
            return;
        }

        if (IsPlaying)
        {
            _audioEngine.Pause();
        }
        else
        {
            if (_hasPlaybackEnded)
            {
                var replayPosition = _audioEngine.Position;
                var duration = _audioEngine.Duration;
                _pendingResumePosition =
                    replayPosition + TimeSpan.FromSeconds(1) < duration
                        ? replayPosition
                        : null;
            }

            if (!_audioEngine.Play())
            {
                _pendingResumePosition = null;
                ErrorMessage = "Playback could not be started.";
            }
        }
    }

    [RelayCommand]
    private void SkipBackward()
    {
        if (CanControlPlayback)
        {
            SeekPlayback(CurrentPlaybackPosition - TimeSpan.FromSeconds(15));
        }
    }

    [RelayCommand]
    private void SkipForward()
    {
        if (CanControlPlayback)
        {
            SeekPlayback(CurrentPlaybackPosition + TimeSpan.FromSeconds(30));
        }
    }

    [RelayCommand]
    private void StartSleepTimer15() => StartSleepTimer(TimeSpan.FromMinutes(15));

    [RelayCommand]
    private void StartSleepTimer30() => StartSleepTimer(TimeSpan.FromMinutes(30));

    [RelayCommand]
    private void StartSleepTimer45() => StartSleepTimer(TimeSpan.FromMinutes(45));

    [RelayCommand]
    private void StartSleepTimer60() => StartSleepTimer(TimeSpan.FromMinutes(60));

    [RelayCommand]
    private void StartSleepTimer90() => StartSleepTimer(TimeSpan.FromMinutes(90));

    [RelayCommand]
    private void AddTenMinutesToSleepTimer()
    {
        if (!IsSleepTimerActive || _sleepTimerDeadlineUtc is not { } deadline)
        {
            return;
        }

        _sleepTimerDeadlineUtc = deadline + TimeSpan.FromMinutes(10);
        UpdateSleepTimerRemaining();
    }

    [RelayCommand]
    private void CancelSleepTimer()
    {
        _sleepTimerPausePending = false;
        StopSleepTimer();
    }

    [RelayCommand(CanExecute = nameof(CanCreateBookmark))]
    private async Task AddBookmarkAsync()
    {
        if (!CanCreateBookmark || string.IsNullOrWhiteSpace(_currentFilePath))
        {
            return;
        }

        try
        {
            ErrorMessage = string.Empty;
            var position = CurrentPlaybackPosition;
            var chapter = FindChapterContaining(position);
            var editResult = await _bookmarkEditorService.EditAsync(bookmark: null);
            if (editResult is null)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            _bookmarkStore.Save(new PlaybackBookmark(
                Guid.NewGuid(),
                _currentFilePath,
                position,
                editResult.Name,
                editResult.Note,
                chapter?.Index,
                chapter?.Title,
                now,
                now));
            RefreshBookmarks();
            ProgressText =
                $"Bookmark saved at {FormatTime(position.TotalSeconds, CurrentPlaybackDuration.TotalSeconds)}";
        }
        catch (Exception exception)
        {
            ErrorMessage = $"The bookmark could not be saved: {exception.Message}";
        }
    }

    private bool CanSelectPreviousChapter() =>
        CanControlPlayback && SelectedChapter is { Index: > 0 };

    [RelayCommand(CanExecute = nameof(CanSelectPreviousChapter))]
    private void PreviousChapter()
    {
        if (SelectedChapter is { Index: > 0 } chapter)
        {
            SelectChapter(chapter.Index - 1);
        }
    }

    private bool CanSelectNextChapter() =>
        CanControlPlayback
        && SelectedChapter is { } chapter
        && chapter.Index + 1 < Chapters.Count;

    [RelayCommand(CanExecute = nameof(CanSelectNextChapter))]
    private void NextChapter()
    {
        if (SelectedChapter is { } chapter && chapter.Index + 1 < Chapters.Count)
        {
            SelectChapter(chapter.Index + 1);
        }
    }

    partial void OnPositionSecondsChanged(double value)
    {
        if (!_isUpdatingPositionFromEngine && CanControlPlayback)
        {
            SeekPlayback(TimeSpan.FromSeconds(value));
        }
    }

    partial void OnVolumeChanged(double value)
    {
        if (_disposed)
        {
            return;
        }

        var safeVolume = double.IsFinite(value)
            ? Math.Clamp(value, MinimumPlaybackVolume, MaximumPlaybackVolume)
            : DefaultPlaybackVolume;

        if (safeVolume != value)
        {
            Volume = safeVolume;
            return;
        }

        _audioEngine.Volume = (int)Math.Round(safeVolume);

        try
        {
            _appSettingsStore.SavePlaybackVolume(safeVolume);
        }
        catch (Exception exception)
        {
            ErrorMessage =
                $"Volume changed for this session, but could not be remembered: {exception.Message}";
        }
    }

    partial void OnSelectedPlaybackRateChanged(double value)
    {
        if (_disposed)
        {
            return;
        }

        if (!PlaybackRates.Contains(value) || !_audioEngine.TrySetPlaybackRate(value))
        {
            ErrorMessage = $"Playback at {value:0.##}× is not supported for this file.";
            return;
        }

        try
        {
            _appSettingsStore.SavePlaybackRate(value);
        }
        catch (Exception exception)
        {
            ErrorMessage =
                $"Playback speed changed for this session, but could not be remembered: {exception.Message}";
        }
    }

    partial void OnSelectedChapterChanged(PlaybackChapterItemViewModel? value)
    {
        if (!_isUpdatingChapterFromEngine && value is not null)
        {
            SelectChapter(value.Index);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        SaveCurrentProgress(force: true);
        _disposed = true;
        _sleepTimer.Stop();
        _sleepTimer.Tick -= OnSleepTimerTick;
        _audioEngine.ProgressChanged -= OnProgressChanged;
        _audioEngine.StateChanged -= OnStateChanged;
        _audioEngine.ChaptersChanged -= OnChaptersChanged;
        _audioEngine.Dispose();
        SetCurrentCover(null);
        DisposeLibraryItems();
    }

    private void OnProgressChanged(object? sender, PlaybackProgressChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _isUpdatingPositionFromEngine = true;
            DurationSeconds = Math.Max(0d, e.Duration.TotalSeconds);
            PositionSeconds = Math.Clamp(
                e.Position.TotalSeconds,
                0d,
                Math.Max(0d, DurationSeconds));
            _isUpdatingPositionFromEngine = false;

            SaveProgress(e.Position, e.Duration, force: false);
        });
    }

    private void OnStateChanged(object? sender, PlaybackStateChangedEventArgs e)
    {
        if (e.State == PlaybackState.Ended)
        {
            _hasPlaybackEnded = true;
        }
        else if (e.State is PlaybackState.Loading
            or PlaybackState.Playing
            or PlaybackState.Stopped
            or PlaybackState.Error)
        {
            _hasPlaybackEnded = false;
        }

        Dispatcher.UIThread.Post(() =>
        {
            switch (e.State)
            {
                case PlaybackState.Loading:
                    StatusText = "Loading";
                    break;
                case PlaybackState.Ready:
                    StatusText = "Ready to play";
                    IsPlaying = false;
                    break;
                case PlaybackState.Playing:
                    StatusText = "Playing";
                    IsPlaying = true;
                    _sleepTimerPausePending = false;
                    if (_pendingResumePosition is { } resumePosition)
                    {
                        _pendingResumePosition = null;
                        _audioEngine.Seek(resumePosition);
                    }
                    _isLoadingFile = false;
                    break;
                case PlaybackState.Paused:
                    StatusText = _sleepTimerPausePending
                        ? "Paused by sleep timer"
                        : "Paused";
                    _sleepTimerPausePending = false;
                    IsPlaying = false;
                    if (!_isLoadingFile)
                    {
                        SaveCurrentProgress(force: true);
                    }
                    break;
                case PlaybackState.Stopped:
                    StatusText = "Stopped";
                    IsPlaying = false;
                    if (!_isLoadingFile)
                    {
                        SaveCurrentProgress(force: true);
                    }
                    break;
                case PlaybackState.Ended:
                    StopSleepTimer();
                    _sleepTimerPausePending = false;
                    StatusText = "Finished";
                    IsPlaying = false;
                    if (!_isLoadingFile)
                    {
                        SaveCurrentProgress(force: true);
                    }
                    break;
                case PlaybackState.Error:
                    StopSleepTimer();
                    _sleepTimerPausePending = false;
                    StatusText = "Playback error";
                    ErrorMessage = e.Message ?? "An unexpected playback error occurred.";
                    IsPlaying = false;
                    _isLoadingFile = false;
                    break;
            }
        });
    }

    private void OnChaptersChanged(object? sender, PlaybackChaptersChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _isUpdatingChapterFromEngine = true;
            Chapters.Clear();
            foreach (var chapter in e.Chapters)
            {
                Chapters.Add(new PlaybackChapterItemViewModel(
                    chapter.Index,
                    chapter.Title,
                    chapter.Start,
                    chapter.Duration));
            }

            SelectedChapter = e.CurrentChapterIndex >= 0 && e.CurrentChapterIndex < Chapters.Count
                ? Chapters[e.CurrentChapterIndex]
                : null;
            _isUpdatingChapterFromEngine = false;

            OnPropertyChanged(nameof(HasChapters));
            OnPropertyChanged(nameof(ChapterPositionText));
            PreviousChapterCommand.NotifyCanExecuteChanged();
            NextChapterCommand.NotifyCanExecuteChanged();
        });
    }

    private void SelectChapter(int chapterIndex)
    {
        if (!_audioEngine.TrySelectChapter(chapterIndex))
        {
            ErrorMessage = "That chapter could not be opened.";
        }
    }

    private void ClearChapters()
    {
        _isUpdatingChapterFromEngine = true;
        Chapters.Clear();
        SelectedChapter = null;
        _isUpdatingChapterFromEngine = false;
        OnPropertyChanged(nameof(HasChapters));
        OnPropertyChanged(nameof(ChapterPositionText));
        PreviousChapterCommand.NotifyCanExecuteChanged();
        NextChapterCommand.NotifyCanExecuteChanged();
    }

    private void RefreshBookmarks()
    {
        ClearBookmarks();
        if (string.IsNullOrWhiteSpace(_currentFilePath))
        {
            return;
        }

        try
        {
            foreach (var bookmark in _bookmarkStore.GetForFile(_currentFilePath))
            {
                Bookmarks.Add(new PlaybackBookmarkItemViewModel(
                    bookmark,
                    JumpToBookmark,
                    EditBookmarkAsync,
                    DeleteBookmark));
            }

            NotifyBookmarkCollectionChanged();
        }
        catch (Exception exception)
        {
            ErrorMessage =
                $"Playback is available, but bookmarks could not be loaded: {exception.Message}";
        }
    }

    private void ClearBookmarks()
    {
        Bookmarks.Clear();
        NotifyBookmarkCollectionChanged();
    }

    private void NotifyBookmarkCollectionChanged()
    {
        OnPropertyChanged(nameof(HasBookmarks));
        OnPropertyChanged(nameof(HasNoBookmarks));
        OnPropertyChanged(nameof(BookmarkCountText));
    }

    private void JumpToBookmark(PlaybackBookmark bookmark)
    {
        if (!CanControlPlayback
            || string.IsNullOrWhiteSpace(_currentFilePath)
            || !PathsEqual(_currentFilePath, bookmark.FilePath))
        {
            return;
        }

        SeekPlayback(bookmark.Position);
        SelectChapterContaining(bookmark.Position);
        ProgressText =
            $"Jumped to bookmark at {FormatTime(bookmark.Position.TotalSeconds, CurrentPlaybackDuration.TotalSeconds)}";
    }

    private async Task EditBookmarkAsync(PlaybackBookmark bookmark)
    {
        try
        {
            ErrorMessage = string.Empty;
            var editResult = await _bookmarkEditorService.EditAsync(bookmark);
            if (editResult is null)
            {
                return;
            }

            _bookmarkStore.Save(bookmark with
            {
                Name = editResult.Name,
                Note = editResult.Note,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            });
            RefreshBookmarks();
            ProgressText = "Bookmark changes saved.";
        }
        catch (Exception exception)
        {
            ErrorMessage = $"The bookmark could not be updated: {exception.Message}";
        }
    }

    private void DeleteBookmark(PlaybackBookmark bookmark)
    {
        try
        {
            _bookmarkStore.Delete(bookmark.Id);
            RefreshBookmarks();
            ProgressText = "Bookmark deleted.";
        }
        catch (Exception exception)
        {
            ErrorMessage = $"The bookmark could not be deleted: {exception.Message}";
        }
    }

    private void StartSleepTimer(TimeSpan duration)
    {
        if (!IsFileLoaded || duration <= TimeSpan.Zero)
        {
            return;
        }

        _sleepTimerPausePending = false;
        _sleepTimerDeadlineUtc = DateTimeOffset.UtcNow + duration;
        SleepTimerRemaining = duration;
        IsSleepTimerActive = true;
        _sleepTimer.Start();
    }

    private void OnSleepTimerTick(object? sender, EventArgs e)
    {
        if (!UpdateSleepTimerRemaining())
        {
            return;
        }

        StopSleepTimer();
        if (IsPlaying && CanControlPlayback)
        {
            _sleepTimerPausePending = true;
            StatusText = "Pausing for sleep timer…";
            _audioEngine.Pause();
        }
        else
        {
            StatusText = "Sleep timer finished";
        }
    }

    private bool UpdateSleepTimerRemaining()
    {
        if (_sleepTimerDeadlineUtc is not { } deadline)
        {
            StopSleepTimer();
            return false;
        }

        var remaining = deadline - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            SleepTimerRemaining = TimeSpan.Zero;
            return true;
        }

        SleepTimerRemaining = remaining;
        return false;
    }

    private void StopSleepTimer()
    {
        _sleepTimer.Stop();
        _sleepTimerDeadlineUtc = null;
        SleepTimerRemaining = TimeSpan.Zero;
        IsSleepTimerActive = false;
    }

    private TimeSpan CurrentPlaybackPosition =>
        _pendingResumePosition ?? _audioEngine.Position;

    private TimeSpan CurrentPlaybackDuration =>
        _audioEngine.Duration > TimeSpan.Zero
            ? _audioEngine.Duration
            : TimeSpan.FromSeconds(Math.Max(0d, DurationSeconds));

    private void SeekPlayback(TimeSpan position)
    {
        var maximum = CurrentPlaybackDuration > TimeSpan.Zero
            ? CurrentPlaybackDuration
            : TimeSpan.MaxValue;
        var clampedPosition = position < TimeSpan.Zero
            ? TimeSpan.Zero
            : position > maximum
                ? maximum
                : position;

        if (_pendingResumePosition is not null)
        {
            _pendingResumePosition = clampedPosition > TimeSpan.Zero
                ? clampedPosition
                : null;
        }

        _audioEngine.Seek(clampedPosition);
    }

    private void ApplyRestoredProgress(PlaybackProgress? progress)
    {
        var durationSeconds = Math.Max(0d, progress?.Duration.TotalSeconds ?? 0d);
        var positionSeconds = Math.Clamp(
            progress?.Position.TotalSeconds ?? 0d,
            0d,
            durationSeconds > 0d ? durationSeconds : double.MaxValue);

        _isUpdatingPositionFromEngine = true;
        DurationSeconds = durationSeconds;
        PositionSeconds = positionSeconds;
        _isUpdatingPositionFromEngine = false;

        SelectChapterContaining(TimeSpan.FromSeconds(positionSeconds));
    }

    private void SelectChapterContaining(TimeSpan position)
    {
        var chapter = FindChapterContaining(position);
        if (chapter is null)
        {
            return;
        }

        _isUpdatingChapterFromEngine = true;
        SelectedChapter = chapter;
        _isUpdatingChapterFromEngine = false;
        OnPropertyChanged(nameof(ChapterPositionText));
        PreviousChapterCommand.NotifyCanExecuteChanged();
        NextChapterCommand.NotifyCanExecuteChanged();
    }

    private PlaybackChapterItemViewModel? FindChapterContaining(TimeSpan position)
    {
        if (Chapters.Count == 0)
        {
            return null;
        }

        var chapter = Chapters[0];
        foreach (var candidate in Chapters)
        {
            if (candidate.Start > position)
            {
                break;
            }

            chapter = candidate;
        }

        return chapter;
    }

    private void SaveCurrentProgress(bool force) =>
        SaveProgress(CurrentPlaybackPosition, CurrentPlaybackDuration, force);

    private void SaveProgress(TimeSpan position, TimeSpan duration, bool force)
    {
        if (_isLoadingFile || !IsFileLoaded || string.IsNullOrWhiteSpace(_currentFilePath))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (!force && now - _lastSavedAtUtc < AutomaticSaveInterval)
        {
            return;
        }

        try
        {
            _progressStore.Save(new PlaybackProgress(
                _currentFilePath,
                position,
                duration,
                now));
            _lastSavedAtUtc = now;
            ProgressText = $"Place saved at {FormatTime(position.TotalSeconds, duration.TotalSeconds)}";
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Playback continues, but your place could not be saved: {exception.Message}";
        }
    }

    private void SaveTheme(AppTheme theme)
    {
        SelectedTheme = theme;
        _themeService.ApplyTheme(theme);

        try
        {
            _appSettingsStore.SaveTheme(theme);
            AppearanceSettingsMessage = $"{theme} appearance is active and will be remembered.";
        }
        catch (Exception exception)
        {
            AppearanceSettingsMessage =
                $"{theme} appearance is active for this session, but could not be saved: {exception.Message}";
        }
    }

    private void SaveLibraryViewMode(LibraryViewMode viewMode)
    {
        SelectedLibraryView = viewMode;

        try
        {
            _appSettingsStore.SaveLibraryViewMode(viewMode);
        }
        catch (Exception exception)
        {
            LibraryStatusMessage =
                $"The {viewMode.ToString().ToLowerInvariant()} view is active for this session, but could not be remembered: {exception.Message}";
        }
    }

    private void RefreshLibrary()
    {
        try
        {
            var books = _audiobookLibrary.GetBooks();
            DisposeLibraryItems();
            LibraryBooks.Clear();

            foreach (var book in books)
            {
                LibraryBooks.Add(new LibraryBookItemViewModel(
                    book,
                    GetProgressSummary(book),
                    LibraryTileWidth,
                    PlayLibraryBookAsync,
                    ChooseCoverAsync,
                    EditMetadataAsync,
                    RemoveBookAsync));
            }

            RebuildLibraryGroups();

            OnPropertyChanged(nameof(HasLibraryBooks));
            OnPropertyChanged(nameof(IsLibraryEmpty));
            OnPropertyChanged(nameof(LibraryBookCountText));
        }
        catch (Exception exception)
        {
            LibraryStatusMessage = $"The library could not be loaded: {exception.Message}";
        }
    }

    private void UnloadCurrentBookForRemoval()
    {
        SaveCurrentProgress(force: true);
        _isLoadingFile = true;

        try
        {
            IsFileLoaded = false;
            _audioEngine.Unload();
            _currentFilePath = null;
            _pendingResumePosition = null;
            _hasPlaybackEnded = false;
            IsPlaying = false;
            PositionSeconds = 0d;
            DurationSeconds = 0d;
            BookTitle = "No audiobook selected";
            FileName = "Choose an audiobook from your library to begin listening.";
            FileFormatText = "AUDIO • LOCAL";
            StatusText = "Ready";
            ProgressText = "Your place will be saved automatically.";
            ErrorMessage = string.Empty;
            StopSleepTimer();
            ClearChapters();
            ClearBookmarks();
            SetCurrentCover(null);
        }
        finally
        {
            _isLoadingFile = false;
        }
    }

    private void SetCurrentCover(string? coverPath)
    {
        Bitmap? newCover = null;

        if (!string.IsNullOrWhiteSpace(coverPath) && File.Exists(coverPath))
        {
            try
            {
                newCover = new Bitmap(coverPath);
            }
            catch
            {
                newCover = null;
            }
        }

        var oldCover = _currentCoverImage;
        if (SetProperty(ref _currentCoverImage, newCover, nameof(CurrentCoverImage)))
        {
            OnPropertyChanged(nameof(HasCurrentCover));
            OnPropertyChanged(nameof(HasNoCurrentCover));
            oldCover?.Dispose();
        }
        else
        {
            newCover?.Dispose();
        }
    }

    private void DisposeLibraryItems()
    {
        foreach (var item in LibraryBooks)
        {
            item.Dispose();
        }
    }

    private void RebuildLibraryGroups()
    {
        var activeGroupName = ActiveLibraryGroup?.Name;
        LibraryGroups.Clear();

        var groupMode = SelectedLibraryGroupOption.Mode;
        if (groupMode == LibraryGroupMode.None)
        {
            LibraryGroups.Add(new LibraryGroupViewModel(
                "All audiobooks",
                LibraryBooks.ToArray(),
                showHeader: false));
            ActiveLibraryGroup = null;
            return;
        }

        var groupedBooks = new Dictionary<string, List<LibraryBookItemViewModel>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var book in LibraryBooks)
        {
            foreach (var groupName in GetGroupNames(book, groupMode))
            {
                if (!groupedBooks.TryGetValue(groupName, out var group))
                {
                    group = [];
                    groupedBooks[groupName] = group;
                }

                group.Add(book);
            }
        }

        var groups = groupedBooks
            .Select(pair => CreateLibraryGroup(
                pair.Key,
                OrderGroupBooks(pair.Value, groupMode).ToArray()));
        groups = groupMode == LibraryGroupMode.Year
            ? groups
                .OrderBy(group => IsFallbackGroup(group.Name))
                .ThenByDescending(group => ParseYear(group.Name))
            : groups
                .OrderBy(group => IsFallbackGroup(group.Name))
                .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            LibraryGroups.Add(group);
        }


        ActiveLibraryGroup = string.IsNullOrWhiteSpace(activeGroupName)
            ? null
            : LibraryGroups.FirstOrDefault(group =>
                string.Equals(group.Name, activeGroupName, StringComparison.OrdinalIgnoreCase));
    }

    private LibraryGroupViewModel CreateLibraryGroup(
        string name,
        IReadOnlyList<LibraryBookItemViewModel> books)
    {
        return new LibraryGroupViewModel(
            name,
            books,
            showHeader: true,
            openRequested: group => ActiveLibraryGroup = group);
    }

    private static IReadOnlyList<string> GetGroupNames(
        LibraryBookItemViewModel book,
        LibraryGroupMode groupMode)
    {
        var metadata = book.Book.Metadata;
        IEnumerable<string?> values;
        switch (groupMode)
        {
            case LibraryGroupMode.Series:
                values = new string?[] { metadata.SeriesName };
                break;
            case LibraryGroupMode.Author:
                values = metadata.Authors;
                break;
            case LibraryGroupMode.Narrator:
                values = metadata.Narrators;
                break;
            case LibraryGroupMode.Genre:
                values = metadata.Genres;
                break;
            case LibraryGroupMode.Publisher:
                values = new string?[] { metadata.AudioPublisher, metadata.OriginalPublisher };
                break;
            case LibraryGroupMode.Year:
                values = new string?[] { metadata.OriginalPublicationYear?.ToString() };
                break;
            default:
                values = [];
                break;
        }

        var normalized = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalized.Length > 0 ? normalized : [GetFallbackGroupName(groupMode)];
    }

    private static IEnumerable<LibraryBookItemViewModel> OrderGroupBooks(
        IEnumerable<LibraryBookItemViewModel> books,
        LibraryGroupMode groupMode)
    {
        return groupMode == LibraryGroupMode.Series
            ? books
                .OrderBy(book => ParseSeriesPosition(book.Book.Metadata.SeriesPosition))
                .ThenBy(book => book.Title, StringComparer.OrdinalIgnoreCase)
            : books.OrderBy(book => book.Title, StringComparer.OrdinalIgnoreCase);
    }

    private static string GetFallbackGroupName(LibraryGroupMode groupMode) => groupMode switch
    {
        LibraryGroupMode.Series => "No series",
        LibraryGroupMode.Author => "Unknown author",
        LibraryGroupMode.Narrator => "Unknown narrator",
        LibraryGroupMode.Genre => "Uncategorized",
        LibraryGroupMode.Publisher => "Unknown publisher",
        LibraryGroupMode.Year => "Year unknown",
        _ => "Other",
    };

    private static bool IsFallbackGroup(string groupName) =>
        groupName is "No series"
            or "Unknown author"
            or "Unknown narrator"
            or "Uncategorized"
            or "Unknown publisher"
            or "Year unknown";

    private static int ParseYear(string value) =>
        int.TryParse(value, out var year) ? year : int.MinValue;

    private static decimal ParseSeriesPosition(string? value) =>
        decimal.TryParse(
            value,
            System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture,
            out var position)
                ? position
                : decimal.MaxValue;

    private static bool PathsEqual(string firstPath, string secondPath) =>
        string.Equals(
            Path.GetFullPath(firstPath),
            Path.GetFullPath(secondPath),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    private string GetProgressSummary(LibraryBook book)
    {
        if (!File.Exists(book.FilePath))
        {
            return "File missing";
        }

        try
        {
            var progress = _progressStore.Get(book.FilePath);
            return progress is { Position: var position } && position > TimeSpan.Zero
                ? $"Resume at {FormatTime(position.TotalSeconds, progress.Duration.TotalSeconds)}"
                : "Not started";
        }
        catch
        {
            return "Progress unavailable";
        }
    }

    private static string BuildImportSummary(
        int addedCount,
        int existingCount,
        IReadOnlyList<string> failures)
    {
        var summary = $"Added {addedCount} audiobook(s).";
        if (existingCount > 0)
        {
            summary += $" {existingCount} already in the library.";
        }

        if (failures.Count > 0)
        {
            summary += $" {failures.Count} failed: {string.Join(" | ", failures)}";
        }

        return summary;
    }

    private static string FormatTime(double seconds, double totalSeconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0d, seconds));
        return totalSeconds >= 3600d
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes}:{time.Seconds:00}";
    }

    private static string FormatSleepTimerRemaining(TimeSpan remaining)
    {
        var totalSeconds = Math.Max(0L, (long)Math.Ceiling(remaining.TotalSeconds));
        var time = TimeSpan.FromSeconds(totalSeconds);
        return time.TotalHours >= 1d
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes}:{time.Seconds:00}";
    }
}
