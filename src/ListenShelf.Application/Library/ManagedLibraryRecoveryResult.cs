namespace ListenShelf.Application.Library;

public sealed record ManagedLibraryRecoveryResult(
    LibraryBook Book,
    bool OrphanCleanupPending);
