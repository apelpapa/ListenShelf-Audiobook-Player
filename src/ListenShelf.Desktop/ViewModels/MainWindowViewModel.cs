using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ListenShelf.Application.Library;
using ListenShelf.Application.Playback;
using ListenShelf.Application.Progress;
using ListenShelf.Application.Settings;
using ListenShelf.Desktop.Services;

namespace ListenShelf.Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan AutomaticSaveInterval = TimeSpan.FromSeconds(10);
    private const double DefaultLibraryTileWidth = 220d;
    private const double MinimumLibraryTileWidth = 180d;
    private const double MaximumLibraryTileWidth = 320d;
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
    private readonly ILibrarySettingsStore _librarySettingsStore;
    private readonly IAppSettingsStore _appSettingsStore;
    private readonly IThemeService _themeService;
    private readonly IAudiobookLibrary _audiobookLibrary;
    private readonly IBookMetadataEditorService _bookMetadataEditorService;
    private readonly ITemporaryPlayerSessionService _temporaryPlayerSessionService;
    private bool _isUpdatingPositionFromEngine;
    private bool _isUpdatingChapterFromEngine;
    private bool _isLoadingFile;
    private bool _hasPlaybackEnded;
    private string? _currentFilePath;
    private TimeSpan? _pendingResumePosition;
    private DateTimeOffset _lastSavedAtUtc = DateTimeOffset.MinValue;
    private Bitmap? _currentCoverImage;
    private bool _disposed;

    public MainWindowViewModel(
        IAudioEngine audioEngine,
        IFilePickerService filePickerService,
        IPlaybackProgressStore progressStore,
        ILibrarySettingsStore librarySettingsStore,
        IAppSettingsStore appSettingsStore,
        IThemeService themeService,
        IAudiobookLibrary audiobookLibrary,
        IBookMetadataEditorService bookMetadataEditorService,
        ITemporaryPlayerSessionService temporaryPlayerSessionService,
        bool isTemporarySession = false)
    {
        _audioEngine = audioEngine;
        _filePickerService = filePickerService;
        _progressStore = progressStore;
        _librarySettingsStore = librarySettingsStore;
        _appSettingsStore = appSettingsStore;
        _themeService = themeService;
        _audiobookLibrary = audiobookLibrary;
        _bookMetadataEditorService = bookMetadataEditorService;
        _temporaryPlayerSessionService = temporaryPlayerSessionService;
        IsTemporarySession = isTemporarySession;

        try
        {
            _defaultLibraryStorageMode = _librarySettingsStore.GetDefaultStorageMode();
        }
        catch (Exception exception)
        {
            _librarySettingsMessage = $"Library preference could not be loaded: {exception.Message}";
            _librarySettingsErrorMessage = _librarySettingsMessage;
        }

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

        _themeService.ApplyTheme(_selectedTheme);

        _audioEngine.ProgressChanged += OnProgressChanged;
        _audioEngine.StateChanged += OnStateChanged;
        _audioEngine.ChaptersChanged += OnChaptersChanged;
        _audioEngine.Volume = (int)Volume;
        _audioEngine.TrySetPlaybackRate(SelectedPlaybackRate);

        if (IsTemporarySession)
        {
            SelectedSection = AppSection.Player;
            ProgressText = "Temporary session — position will not be saved.";
        }

        RefreshLibrary();
    }

    public IReadOnlyList<double> PlaybackRates { get; } =
        [0.75d, 1d, 1.25d, 1.5d, 1.75d, 2d];

    public ObservableCollection<LibraryBookItemViewModel> LibraryBooks { get; } = [];

    public ObservableCollection<PlaybackChapterItemViewModel> Chapters { get; } = [];

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

    public bool IsTemporarySession { get; }

    public bool IsPersistentSession => !IsTemporarySession;

    public string WindowTitle => IsTemporarySession
        ? "ListenShelf — Temporary Player Only Session"
        : "ListenShelf — Audiobook Player";

    public string FooterText => IsTemporarySession
        ? "Temporary Player Only session • Nothing opened or changed here will be saved"
        : "Offline playback • Settings alone never move or copy your files";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibrarySection))]
    [NotifyPropertyChangedFor(nameof(IsPlayerSection))]
    [NotifyPropertyChangedFor(nameof(IsSettingsSection))]
    [NotifyPropertyChangedFor(nameof(PageTitle))]
    [NotifyPropertyChangedFor(nameof(PageSubtitle))]
    private AppSection _selectedSection = AppSection.Library;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnboardingVisible))]
    [NotifyPropertyChangedFor(nameof(IsMainContentVisible))]
    [NotifyPropertyChangedFor(nameof(IsLinkedModeSelected))]
    [NotifyPropertyChangedFor(nameof(IsManagedModeSelected))]
    [NotifyPropertyChangedFor(nameof(CurrentLibraryModeTitle))]
    [NotifyPropertyChangedFor(nameof(LibraryEmptyDescription))]
    [NotifyPropertyChangedFor(nameof(CanAddAudiobooks))]
    private LibraryStorageMode? _defaultLibraryStorageMode;

    [ObservableProperty]
    private string _librarySettingsMessage = "Choose how ListenShelf handles audiobook files.";

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
    [NotifyPropertyChangedFor(nameof(HasLibrarySettingsError))]
    private string _librarySettingsErrorMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddAudiobooks))]
    private bool _isLibraryBusy;

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
    [NotifyCanExecuteChangedFor(nameof(PreviousChapterCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextChapterCommand))]
    private bool _isFileLoaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayPauseLabel))]
    private bool _isPlaying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanControlPlayback))]
    [NotifyCanExecuteChangedFor(nameof(PreviousChapterCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextChapterCommand))]
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

    public Bitmap? CurrentCoverImage => _currentCoverImage;

    public bool HasCurrentCover => CurrentCoverImage is not null;

    public bool HasNoCurrentCover => !HasCurrentCover;

    public bool CanControlPlayback => IsFileLoaded && !IsBusy;

    public bool HasChapters => Chapters.Count > 0;

    public string ChapterPositionText => SelectedChapter is null
        ? string.Empty
        : $"Chapter {SelectedChapter.Index + 1} of {Chapters.Count}";

    public bool IsLibrarySection => SelectedSection == AppSection.Library;

    public bool IsPlayerSection => SelectedSection == AppSection.Player;

    public bool IsSettingsSection => SelectedSection == AppSection.Settings;

    public bool IsOnboardingVisible => DefaultLibraryStorageMode is null;

    public bool IsMainContentVisible => !IsOnboardingVisible;

    public bool IsLinkedModeSelected => DefaultLibraryStorageMode == LibraryStorageMode.Linked;

    public bool IsManagedModeSelected => DefaultLibraryStorageMode == LibraryStorageMode.Managed;

    public bool IsDarkThemeSelected => SelectedTheme == AppTheme.Dark;

    public bool IsLightThemeSelected => SelectedTheme == AppTheme.Light;

    public bool IsLibraryListView => SelectedLibraryView == LibraryViewMode.List;

    public bool IsLibraryTileView => SelectedLibraryView == LibraryViewMode.Tiles;

    public bool HasLibrarySettingsError => !string.IsNullOrWhiteSpace(LibrarySettingsErrorMessage);

    public bool HasLibraryBooks => LibraryBooks.Count > 0;

    public bool IsLibraryEmpty => !HasLibraryBooks;

    public bool CanAddAudiobooks => !IsLibraryBusy && DefaultLibraryStorageMode is not null;

    public string LibraryBookCountText => LibraryBooks.Count == 1
        ? "1 audiobook"
        : $"{LibraryBooks.Count} audiobooks";

    public string ManagedLibraryPath => _audiobookLibrary.ManagedLibraryPath;

    public string PageTitle => IsTemporarySession
        ? "Player Only Session"
        : SelectedSection switch
        {
            AppSection.Player => "Player",
            AppSection.Settings => "Settings",
            _ => "Library",
        };

    public string PageSubtitle => IsTemporarySession
        ? "Play a local audiobook without adding it to the library or saving any session activity."
        : SelectedSection switch
        {
            AppSection.Player => "Listen locally with automatic progress saving.",
            AppSection.Settings => "Personalize ListenShelf and choose how files are handled.",
            _ => "Your audiobooks, series, and collections will live here.",
        };

    public string CurrentLibraryModeTitle => DefaultLibraryStorageMode switch
    {
        LibraryStorageMode.Linked => "Player Only Mode",
        LibraryStorageMode.Managed => "Let ListenShelf manage copies",
        _ => "Choose how your library works",
    };

    public string LibraryEmptyDescription => DefaultLibraryStorageMode switch
    {
        LibraryStorageMode.Managed =>
            "Choose M4B, M4A, or MP3 files. ListenShelf will make verified copies and leave the originals untouched.",
        _ =>
            "Choose M4B, M4A, or MP3 files. ListenShelf will remember their locations and listening positions without managing book metadata.",
    };

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
    private void ShowSettings() => SelectedSection = AppSection.Settings;

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
    private async Task ChooseLinkedLibraryAsync()
    {
        if (DefaultLibraryStorageMode != LibraryStorageMode.Managed)
        {
            SaveLibraryStorageMode(LibraryStorageMode.Linked);
            return;
        }

        try
        {
            var hasManagedData = _audiobookLibrary
                .GetBooks()
                .Any(book => book.StorageMode == LibraryStorageMode.Managed);

            if (!hasManagedData)
            {
                SaveLibraryStorageMode(LibraryStorageMode.Linked);
                return;
            }

            var opened = await _temporaryPlayerSessionService.WarnAndOpenAsync(SelectedTheme);
            LibrarySettingsMessage = opened
                ? "Managed Library remains selected. The temporary Player Only window will not save session activity."
                : "Managed Library remains selected. No files or settings were changed.";
        }
        catch (Exception exception)
        {
            LibrarySettingsMessage = $"Player Only Mode could not be opened: {exception.Message}";
        }
    }

    [RelayCommand]
    private void ChooseManagedLibrary() => SaveLibraryStorageMode(LibraryStorageMode.Managed);

    [RelayCommand]
    private async Task AddAudiobooksAsync()
    {
        if (!CanAddAudiobooks || DefaultLibraryStorageMode is not { } storageMode)
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
            LibraryStatusMessage = storageMode == LibraryStorageMode.Managed
                ? $"Copying {filePaths.Count} audiobook(s) into the managed library…"
                : $"Adding {filePaths.Count} audiobook(s) in Player Only Mode…";

            var addedCount = 0;
            var existingCount = 0;
            var failures = new List<string>();

            foreach (var filePath in filePaths)
            {
                try
                {
                    var result = await Task.Run(() => _audiobookLibrary.Import(filePath, storageMode));
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

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        try
        {
            ErrorMessage = string.Empty;
            var filePath = await _filePickerService.PickAudiobookFileAsync();
            if (filePath is null)
            {
                return;
            }

            await LoadAndPlayFileAsync(filePath);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            StatusText = "Could not open audiobook";
        }
    }

    private async Task LoadAndPlayFileAsync(string filePath, LibraryBook? libraryBook = null)
    {
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
            SetCurrentCover(null);

            await _audioEngine.LoadAsync(filePath);

            _currentFilePath = Path.GetFullPath(filePath);
            var savedProgress = IsTemporarySession
                ? null
                : _progressStore.Get(_currentFilePath);
            _pendingResumePosition = savedProgress?.Position > TimeSpan.Zero
                ? savedProgress.Position
                : null;

            BookTitle = libraryBook?.Title ?? Path.GetFileNameWithoutExtension(_currentFilePath);
            FileName = Path.GetFileName(_currentFilePath);
            FileFormatText = $"{Path.GetExtension(_currentFilePath).TrimStart('.').ToUpperInvariant()} • LOCAL";
            SetCurrentCover(libraryBook?.CoverPath);
            ProgressText = IsTemporarySession
                ? "Temporary session — position will not be saved."
                : _pendingResumePosition is { } resumePosition
                    ? $"Resuming from {FormatTime(resumePosition.TotalSeconds, savedProgress?.Duration.TotalSeconds ?? 0d)}"
                    : "Starting from the beginning";
            IsFileLoaded = true;
            _lastSavedAtUtc = DateTimeOffset.UtcNow;

            if (!_audioEngine.Play())
            {
                _isLoadingFile = false;
                ErrorMessage = "The audiobook could not be started.";
            }
        }
        catch (Exception exception)
        {
            _isLoadingFile = false;
            ErrorMessage = exception.Message;
            StatusText = "Could not open audiobook";
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
        await LoadAndPlayFileAsync(book.FilePath, book);
    }

    private async Task ChooseCoverAsync(LibraryBook book)
    {
        if (book.StorageMode != LibraryStorageMode.Managed)
        {
            LibraryStatusMessage = "Cover editing is available for ListenShelf-managed copies only.";
            return;
        }

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
        if (book.StorageMode != LibraryStorageMode.Managed)
        {
            LibraryStatusMessage = "Metadata editing is available for ListenShelf-managed copies only.";
            return;
        }

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
            _audioEngine.Seek(_audioEngine.Position - TimeSpan.FromSeconds(15));
        }
    }

    [RelayCommand]
    private void SkipForward()
    {
        if (CanControlPlayback)
        {
            _audioEngine.Seek(_audioEngine.Position + TimeSpan.FromSeconds(30));
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
            _audioEngine.Seek(TimeSpan.FromSeconds(value));
        }
    }

    partial void OnVolumeChanged(double value)
    {
        if (!_disposed)
        {
            _audioEngine.Volume = (int)Math.Round(value);
        }
    }

    partial void OnSelectedPlaybackRateChanged(double value)
    {
        if (!_disposed && !_audioEngine.TrySetPlaybackRate(value))
        {
            ErrorMessage = $"Playback at {value:0.##}× is not supported for this file.";
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
                    if (_pendingResumePosition is { } resumePosition)
                    {
                        _pendingResumePosition = null;
                        _audioEngine.Seek(resumePosition);
                    }
                    _isLoadingFile = false;
                    break;
                case PlaybackState.Paused:
                    StatusText = "Paused";
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
                    StatusText = "Finished";
                    IsPlaying = false;
                    if (!_isLoadingFile)
                    {
                        SaveCurrentProgress(force: true);
                    }
                    break;
                case PlaybackState.Error:
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

    private void SaveCurrentProgress(bool force) =>
        SaveProgress(_audioEngine.Position, _audioEngine.Duration, force);

    private void SaveProgress(TimeSpan position, TimeSpan duration, bool force)
    {
        if (IsTemporarySession)
        {
            ProgressText = "Temporary session — position will not be saved.";
            return;
        }

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

    private void SaveLibraryStorageMode(LibraryStorageMode storageMode)
    {
        var wasOnboarding = IsOnboardingVisible;

        try
        {
            _librarySettingsStore.SaveDefaultStorageMode(storageMode);
            DefaultLibraryStorageMode = storageMode;
            LibrarySettingsErrorMessage = string.Empty;
            LibrarySettingsMessage = $"Saved: {CurrentLibraryModeTitle}.";

            if (wasOnboarding)
            {
                SelectedSection = AppSection.Library;
            }
        }
        catch (Exception exception)
        {
            LibrarySettingsErrorMessage =
                $"Library preference could not be saved: {exception.Message}";
            LibrarySettingsMessage = LibrarySettingsErrorMessage;
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
                    EditMetadataAsync));
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
}
