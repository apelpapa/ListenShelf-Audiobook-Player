using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ListenShelf.Application.Playback;
using ListenShelf.Application.Progress;
using ListenShelf.Desktop.Services;

namespace ListenShelf.Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan AutomaticSaveInterval = TimeSpan.FromSeconds(10);

    private readonly IAudioEngine _audioEngine;
    private readonly IFilePickerService _filePickerService;
    private readonly IPlaybackProgressStore _progressStore;
    private bool _isUpdatingPositionFromEngine;
    private bool _isLoadingFile;
    private string? _currentFilePath;
    private TimeSpan? _pendingResumePosition;
    private DateTimeOffset _lastSavedAtUtc = DateTimeOffset.MinValue;
    private bool _disposed;

    public MainWindowViewModel(
        IAudioEngine audioEngine,
        IFilePickerService filePickerService,
        IPlaybackProgressStore progressStore)
    {
        _audioEngine = audioEngine;
        _filePickerService = filePickerService;
        _progressStore = progressStore;

        _audioEngine.ProgressChanged += OnProgressChanged;
        _audioEngine.StateChanged += OnStateChanged;
        _audioEngine.Volume = (int)Volume;
        _audioEngine.TrySetPlaybackRate(SelectedPlaybackRate);
    }

    public IReadOnlyList<double> PlaybackRates { get; } =
        [0.75d, 1d, 1.25d, 1.5d, 1.75d, 2d];

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

    public bool CanControlPlayback => IsFileLoaded && !IsBusy;

    public string PlayPauseLabel => IsPlaying ? "Pause" : "Play";

    public double SeekMaximum => Math.Max(1d, DurationSeconds);

    public string ElapsedText => FormatTime(PositionSeconds, DurationSeconds);

    public string DurationText => FormatTime(DurationSeconds, DurationSeconds);

    public string RemainingText =>
        $"-{FormatTime(Math.Max(0d, DurationSeconds - PositionSeconds), DurationSeconds)}";

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

            IsBusy = true;
            StatusText = "Opening audiobook…";
            SaveCurrentProgress(force: true);
            _isLoadingFile = true;
            IsFileLoaded = false;
            _currentFilePath = null;
            _pendingResumePosition = null;

            await _audioEngine.LoadAsync(filePath);

            _currentFilePath = Path.GetFullPath(filePath);
            var savedProgress = _progressStore.Get(_currentFilePath);
            _pendingResumePosition = savedProgress?.Position > TimeSpan.Zero
                ? savedProgress.Position
                : null;

            BookTitle = Path.GetFileNameWithoutExtension(_currentFilePath);
            FileName = Path.GetFileName(_currentFilePath);
            ProgressText = _pendingResumePosition is { } resumePosition
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

    private static string FormatTime(double seconds, double totalSeconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0d, seconds));
        return totalSeconds >= 3600d
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes}:{time.Seconds:00}";
    }
}
