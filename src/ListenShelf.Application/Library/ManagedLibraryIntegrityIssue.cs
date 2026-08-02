namespace ListenShelf.Application.Library;

public sealed record ManagedLibraryIntegrityIssue(
    ManagedLibraryIntegrityIssueKind Kind,
    string Path,
    string Description,
    Guid? BookId = null);
