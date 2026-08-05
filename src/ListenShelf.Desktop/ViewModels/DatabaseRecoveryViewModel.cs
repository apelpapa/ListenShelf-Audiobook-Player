using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ListenShelf.Application.Backup;
using ListenShelf.Desktop.Services;
using ListenShelf.Infrastructure.Storage;

namespace ListenShelf.Desktop.ViewModels;

public partial class DatabaseRecoveryViewModel : ViewModelBase
{
    private readonly DatabaseRecoveryService _recoveryService;
    private readonly IFilePickerService _filePickerService;
    private string? _selectedBackupPath;

    [ObservableProperty]
    private string _heading = string.Empty;

    [ObservableProperty]
    private string _explanation = string.Empty;

    [ObservableProperty]
    private string _technicalDetails = string.Empty;

    [ObservableProperty]
    private string _statusText =
        "Your managed audiobook files have not been deleted or changed.";

    [ObservableProperty]
    private string _pendingRestoreSummary = string.Empty;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private string _retryButtonText = "Retry";

    [ObservableProperty]
    private double _progressPercentage;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isProgressVisible;

    [ObservableProperty]
    private bool _isRestoreConfirmationVisible;

    [ObservableProperty]
    private bool _isRebuildConfirmationVisible;

    [ObservableProperty]
    private bool _canRebuildCatalog;

    [ObservableProperty]
    private bool _isRecoveryApplied;

    public DatabaseRecoveryViewModel(
        ListenShelfDatabaseException failure,
        DatabaseRecoveryService recoveryService,
        IFilePickerService filePickerService)
    {
        ArgumentNullException.ThrowIfNull(failure);
        _recoveryService = recoveryService
            ?? throw new ArgumentNullException(nameof(recoveryService));
        _filePickerService = filePickerService
            ?? throw new ArgumentNullException(nameof(filePickerService));
        ApplyFailure(failure);
    }

    public event EventHandler? RecoveryCompleted;

    public bool CanInteract => !IsBusy;

    public bool CanRunRecoveryOperation => CanInteract && !IsRecoveryApplied;

    public string DataDirectoryPath => _recoveryService.DataRootPath;

    public void ApplyFailure(ListenShelfDatabaseException failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        (Heading, Explanation, CanRebuildCatalog) = failure.Kind switch
        {
            ListenShelfDatabaseFailureKind.Damaged =>
                ("Your library catalog needs recovery",
                 "ListenShelf found damage or an invalid structure in its database. You can restore a backup or rebuild a basic catalog from the managed audiobook files still on disk.",
                 true),
            ListenShelfDatabaseFailureKind.NewerVersion =>
                ("This library needs a newer ListenShelf version",
                 "The database was created by a newer version of ListenShelf. Install that version or restore a compatible backup. ListenShelf will not downgrade or rewrite this database.",
                 false),
            ListenShelfDatabaseFailureKind.MigrationFailed =>
                ("The database upgrade was stopped safely",
                 "A versioned database migration could not finish and was rolled back. Retry, restore a backup, or inspect the data directory before continuing.",
                 false),
            _ =>
                ("ListenShelf cannot access its database",
                 "The database may be locked, unavailable, or blocked by filesystem permissions. Retry after resolving the access problem, or restore a backup.",
                 false),
        };

        TechnicalDetails = failure.Message;
        StatusText = "Your managed audiobook files have not been deleted or changed.";
        RetryButtonText = "Retry";
        IsRecoveryApplied = false;
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInteract));
        OnPropertyChanged(nameof(CanRunRecoveryOperation));
    }

    partial void OnIsRecoveryAppliedChanged(bool value) =>
        OnPropertyChanged(nameof(CanRunRecoveryOperation));

    [RelayCommand]
    private void Retry()
    {
        if (!CanInteract)
        {
            return;
        }

        StatusText = "Retrying database startup…";
        RecoveryCompleted?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task ChooseBackupAsync()
    {
        if (!CanRunRecoveryOperation)
        {
            return;
        }

        IsBusy = true;
        IsRestoreConfirmationVisible = false;
        _selectedBackupPath = null;
        try
        {
            var path = await _filePickerService.PickBackupImportPathAsync();
            if (path is null)
            {
                StatusText = "No backup was selected. Nothing was changed.";
                return;
            }

            BeginProgress("Validating selected backup…");
            var progress = CreateProgressReporter();
            var summary = await Task.Run(() =>
                _recoveryService.InspectBackup(path, progress));
            _selectedBackupPath = summary.BackupPath;
            PendingRestoreSummary =
                $"Created {summary.CreatedAtUtc.ToLocalTime():g} • {summary.BookCount} audiobook{(summary.BookCount == 1 ? string.Empty : "s")} • {FormatFileSize(summary.ArchiveSizeBytes)}";
            StatusText =
                "The backup passed its manifest, size, SHA-256, and SQLite integrity checks.";
            IsRestoreConfirmationVisible = true;
        }
        catch (Exception exception)
        {
            StatusText = $"The selected backup is not usable: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
            IsProgressVisible = false;
        }
    }

    [RelayCommand]
    private async Task RestoreBackupAsync()
    {
        if (!CanRunRecoveryOperation || _selectedBackupPath is null)
        {
            return;
        }

        IsBusy = true;
        IsRestoreConfirmationVisible = false;
        try
        {
            BeginProgress("Preparing recovery restore…");
            var progress = CreateProgressReporter();
            var result = await Task.Run(() =>
                _recoveryService.RestoreBackup(_selectedBackupPath, progress));
            StatusText = result.PreservedDataPath is null
                ? "Backup restored. Continue to open the recovered library."
                : $"Backup restored. The previous data directory was preserved at {result.PreservedDataPath}. Continue when you are ready.";
            RetryButtonText = "Continue to ListenShelf";
            IsRecoveryApplied = true;
        }
        catch (Exception exception)
        {
            StatusText =
                $"The backup could not be restored. The previous data was kept: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
            IsProgressVisible = false;
        }
    }

    [RelayCommand]
    private void CancelRestore()
    {
        _selectedBackupPath = null;
        PendingRestoreSummary = string.Empty;
        IsRestoreConfirmationVisible = false;
        StatusText = "Restore cancelled. Nothing was changed.";
    }

    [RelayCommand]
    private void ShowRebuildConfirmation()
    {
        if (CanRunRecoveryOperation && CanRebuildCatalog)
        {
            IsRebuildConfirmationVisible = true;
        }
    }

    [RelayCommand]
    private void CancelRebuild()
    {
        IsRebuildConfirmationVisible = false;
        StatusText = "Catalog rebuild cancelled. Nothing was changed.";
    }

    [RelayCommand]
    private async Task RebuildCatalogAsync()
    {
        if (!CanRunRecoveryOperation || !CanRebuildCatalog)
        {
            return;
        }

        IsBusy = true;
        IsRebuildConfirmationVisible = false;
        try
        {
            BeginProgress("Preserving the damaged database and rebuilding the catalog…");
            var result = await Task.Run(_recoveryService.RebuildCatalog);
            StatusText =
                $"Recovered {result.RecoveredBookCount} audiobook{(result.RecoveredBookCount == 1 ? string.Empty : "s")}. "
                + (result.SkippedAudiobookCount == 0
                    ? string.Empty
                    : $"{result.SkippedAudiobookCount} nonstandard file{(result.SkippedAudiobookCount == 1 ? " was" : "s were")} left for Storage Care. ")
                + $"The damaged database was preserved at {result.PreservedDatabasePath}. Continue when you are ready.";
            RetryButtonText = "Continue to ListenShelf";
            IsRecoveryApplied = true;
        }
        catch (Exception exception)
        {
            StatusText = $"The catalog could not be rebuilt: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
            IsProgressVisible = false;
        }
    }

    [RelayCommand]
    private void OpenDataDirectory()
    {
        try
        {
            Directory.CreateDirectory(DataDirectoryPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = DataDirectoryPath,
                UseShellExecute = true,
            });
        }
        catch (Exception exception)
        {
            StatusText = $"The data directory could not be opened: {exception.Message}";
        }
    }

    private void BeginProgress(string text)
    {
        ProgressText = text;
        ProgressPercentage = 0;
        IsProgressVisible = true;
    }

    private IProgress<LibraryBackupProgress> CreateProgressReporter() =>
        new Progress<LibraryBackupProgress>(progress =>
        {
            ProgressPercentage = progress.Percentage;
            ProgressText = progress.TotalFiles > 0
                ? $"{progress.Stage} • {progress.CompletedFiles} of {progress.TotalFiles} files"
                : progress.Stage;
        });

    private static string FormatFileSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.#} {units[unitIndex]}";
    }
}
