namespace ListenShelf.Application.Library;

public sealed record ManagedLibraryCleanupResult(
    string Path,
    bool WasDirectory);
