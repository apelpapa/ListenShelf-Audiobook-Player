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
    private bool _isLoadingFile;
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

        _themeService.ApplyTheme(_selectedTheme);

        _audioEngine.ProgressChanged += OnProgressChanged;
        _audioEngine.StateChanged += OnStateChanged;
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
    [NotifyPropertyChangedFor(nameof(HasLibrarySettingsError))]
    private string _librarySettingsErrorMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddAudiobooks))]
    private bool _isLibraryBusy;

    [ObservableProperty]
    private string _libraryStatusMessage = "Add one or more M4B files to begin building your shelf.";

    [ObservableProperty]
    private string _bookTitle = "No audiobook selected";

    [ObservableProperty]
    private string _fileName = "Open an M4B file to begin listening.";

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _progressText = "Your place will be saved automatically.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanControlPlayback))]
    private bool _isFileLoaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayPauseLabel))]
    private bool _isPlaying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanControlPlayback))]
    private bool _isBusy;

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
        ? "Play a local M4B without adding it to the library or saving any session activity."
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
            "Choose one or more M4B files. ListenShelf will make verified copies and leave the originals untouched.",
        _ =>
            "Choose one or more M4B files. ListenShelf will remember their locations and listening positions without managing book metadata.",
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
            var filePaths = await _filePickerService.PickM4bFilesAsync();
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
            var filePath = await _filePickerService.PickM4bFileAsync();
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
            var metadata = await _bookMetadataEditorService.EditAsync(book, suggestions);
            if (metadata is null)
            {
                return;
            }

            IsLibraryBusy = true;
            LibraryStatusMessage = $"Saving details for {book.Title}…";

            var updatedBook = await Task.Run(() => _audiobookLibrary.UpdateMetadata(book.Id, metadata));
            if (!string.IsNullOrWhiteSpace(_currentFilePath)
                && PathsEqual(_currentFilePath, updatedBook.FilePath))
            {
                BookTitle = updatedBook.Title;
            }

            RefreshLibrary();
            LibraryStatusMessage = $"Details saved for {updatedBook.Title}.";
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
        else if (!_audioEngine.Play())
        {
            ErrorMessage = "Playback could not be started.";
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
                    PlayLibraryBookAsync,
                    ChooseCoverAsync,
                    EditMetadataAsync));
            }

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
