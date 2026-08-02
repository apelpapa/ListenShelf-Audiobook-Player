namespace ListenShelf.Application.Library;

public sealed record ManagedLibraryIntegrityReport(
    DateTimeOffset CheckedAtUtc,
    int CatalogBookCount,
    int ReferencedManagedFileCount,
    IReadOnlyList<ManagedLibraryIntegrityIssue> Issues)
{
    public bool IsHealthy => Issues.Count == 0;
}
