using ListenShelf.Application.Library;

namespace ListenShelf.Desktop.ViewModels;

public sealed class ManagedStorageIssueItemViewModel(ManagedLibraryIntegrityIssue issue)
{
    public string Category => issue.Kind switch
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

    public string Description => issue.Description;

    public string Path => issue.Path;
}
