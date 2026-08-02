namespace ListenShelf.Application.Library;

public enum ManagedLibraryIntegrityIssueKind
{
    MissingManagedDirectory,
    MissingManagedFile,
    CatalogPathOutsideManagedStorage,
    UnreferencedDirectory,
    UnreferencedFile,
    StaleImportFile,
    PendingRemovalCleanup,
    UnreadablePath,
}
