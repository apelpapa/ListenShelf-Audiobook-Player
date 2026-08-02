using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ListenShelf.Application.Library;

namespace ListenShelf.Desktop.ViewModels;

public sealed partial class ManagedStorageIssueItemViewModel : ObservableObject
{
    private readonly Func<ManagedStorageIssueItemViewModel, Task> _recoverAsync;
    private readonly Func<ManagedStorageIssueItemViewModel, Task> _cleanUpAsync;

    public ManagedStorageIssueItemViewModel(
        ManagedLibraryIntegrityIssue issue,
        Func<ManagedStorageIssueItemViewModel, Task> recoverAsync,
        Func<ManagedStorageIssueItemViewModel, Task> cleanUpAsync)
    {
        Issue = issue ?? throw new ArgumentNullException(nameof(issue));
        _recoverAsync = recoverAsync ?? throw new ArgumentNullException(nameof(recoverAsync));
        _cleanUpAsync = cleanUpAsync ?? throw new ArgumentNullException(nameof(cleanUpAsync));

        RecoverCommand = new AsyncRelayCommand(RecoverAsync, CanRecoverNow);
        RequestCleanupCommand = new RelayCommand(RequestCleanup, CanCleanUpNow);
        ConfirmCleanupCommand = new AsyncRelayCommand(CleanUpAsync, CanCleanUpNow);
        CancelCleanupCommand = new RelayCommand(CancelCleanup, () => !IsBusy);
    }

    public ManagedLibraryIntegrityIssue Issue { get; }

    public string Category => Issue.Kind switch
    {
        ManagedLibraryIntegrityIssueKind.MissingManagedDirectory => "MISSING BOOK FOLDER",
        ManagedLibraryIntegrityIssueKind.MissingManagedFile => "MISSING AUDIOBOOK",
        ManagedLibraryIntegrityIssueKind.CatalogPathOutsideManagedStorage => "UNSAFE CATALOG PATH",
        ManagedLibraryIntegrityIssueKind.UnreferencedDirectory => "UNREFERENCED FOLDER",
        ManagedLibraryIntegrityIssueKind.UnreferencedFile => "UNREFERENCED FILE",
        ManagedLibraryIntegrityIssueKind.StaleImportFile => "STALE IMPORT",
        ManagedLibraryIntegrityIssueKind.PendingRemovalCleanup => "PENDING REMOVAL CLEANUP",
        ManagedLibraryIntegrityIssueKind.UnreadablePath => "UNREADABLE PATH",
        _ => "STORAGE ISSUE",
    };

    public string Name => System.IO.Path.GetFileName(Issue.Path.TrimEnd(
        System.IO.Path.DirectorySeparatorChar,
        System.IO.Path.AltDirectorySeparatorChar));

    public string Description => Issue.Description;

    public string Path => Issue.Path;

    public bool CanRecover =>
        Issue.Kind == ManagedLibraryIntegrityIssueKind.UnreferencedFile
        && AudiobookFileFormats.IsSupported(Issue.Path);

    public bool CanCleanUp => Issue.Kind is
        ManagedLibraryIntegrityIssueKind.UnreferencedDirectory
        or ManagedLibraryIntegrityIssueKind.UnreferencedFile
        or ManagedLibraryIntegrityIssueKind.StaleImportFile;

    public bool ShowPrimaryActions =>
        !IsCleanupConfirmationVisible && (CanRecover || CanCleanUp);

    public string CleanupPrompt => Issue.Kind == ManagedLibraryIntegrityIssueKind.UnreferencedDirectory
        ? "Permanently delete this folder and everything currently inside it?"
        : "Permanently delete this unreferenced file?";

    public string CleanupButtonText => Issue.Kind == ManagedLibraryIntegrityIssueKind.UnreferencedDirectory
        ? "Delete folder"
        : "Delete file";

    public string ActionHint => CanRecover
        ? "Recover adds this audiobook back to the catalog. Cleanup permanently deletes only the selected orphan."
        : CanCleanUp
            ? "This item is not used by the catalog. Cleanup is optional and only frees storage space."
            : Issue.Kind == ManagedLibraryIntegrityIssueKind.PendingRemovalCleanup
                ? "ListenShelf will retry this previously confirmed cleanup the next time it starts."
                : "ListenShelf will not change this item automatically. Review the description and path.";

    public IAsyncRelayCommand RecoverCommand { get; }

    public IRelayCommand RequestCleanupCommand { get; }

    public IAsyncRelayCommand ConfirmCleanupCommand { get; }

    public IRelayCommand CancelCleanupCommand { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPrimaryActions))]
    private bool _isCleanupConfirmationVisible;

    [ObservableProperty]
    private bool _isBusy;

    private bool CanRecoverNow() => CanRecover && !IsBusy;

    private bool CanCleanUpNow() => CanCleanUp && !IsBusy;

    private async Task RecoverAsync()
    {
        if (!CanRecoverNow())
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _recoverAsync(this);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RequestCleanup()
    {
        if (CanCleanUpNow())
        {
            IsCleanupConfirmationVisible = true;
        }
    }

    private async Task CleanUpAsync()
    {
        if (!CanCleanUpNow())
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _cleanUpAsync(this);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void CancelCleanup() => IsCleanupConfirmationVisible = false;

    partial void OnIsBusyChanged(bool value)
    {
        RecoverCommand.NotifyCanExecuteChanged();
        RequestCleanupCommand.NotifyCanExecuteChanged();
        ConfirmCleanupCommand.NotifyCanExecuteChanged();
        CancelCleanupCommand.NotifyCanExecuteChanged();
    }
}
