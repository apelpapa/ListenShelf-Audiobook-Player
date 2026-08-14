namespace ListenShelf.Application.Library;

public enum ManagedFileVerificationStatus
{
    Unchanged,
    Changed,
    Missing,
    Unreadable,
    NoBaseline,
    Canceled,
    NotChecked,
}

public sealed record ManagedFileVerificationResult(Guid BookId, string Title, string FilePath,
    ManagedFileVerificationStatus Status, string Detail, DateTimeOffset CheckedAtUtc)
{
    public bool NeedsAttention => Status is ManagedFileVerificationStatus.Changed
        or ManagedFileVerificationStatus.Missing or ManagedFileVerificationStatus.Unreadable;
    public bool WasChecked => Status is not (ManagedFileVerificationStatus.Canceled or ManagedFileVerificationStatus.NotChecked);
    public string StatusText => Status switch
    {
        ManagedFileVerificationStatus.Unchanged => "Verified unchanged",
        ManagedFileVerificationStatus.Changed => "Changed",
        ManagedFileVerificationStatus.Missing => "Missing",
        ManagedFileVerificationStatus.Unreadable => "Unreadable",
        ManagedFileVerificationStatus.NoBaseline => "No verification baseline",
        ManagedFileVerificationStatus.Canceled => "Canceled",
        _ => "Not checked",
    };
}

public sealed record ManagedFileVerificationProgress(int BookNumber, int TotalBooks, string Title,
    long ProcessedBytes, long TotalBytes, ManagedFileVerificationResult? CompletedBook = null)
{
    public double FilePercentage => TotalBytes > 0 ? Math.Clamp(100d * ProcessedBytes / TotalBytes, 0d, 100d) : 0d;
    public double OverallPercentage => TotalBooks > 0
        ? 100d * (BookNumber - 1 + (CompletedBook is null ? FilePercentage / 100d : 1d)) / TotalBooks : 0d;
}

public sealed record ManagedFileVerificationReport(DateTimeOffset FinishedAtUtc,
    IReadOnlyList<ManagedFileVerificationResult> Results)
{
    public bool WasCanceled => Results.Any(result => !result.WasChecked);
    public int Count(ManagedFileVerificationStatus status) => Results.Count(result => result.Status == status);
    public string Summary => Results.Count == 0 ? "No managed audiobooks to verify."
        : $"{(WasCanceled ? "Verification canceled" : "Verification complete")}: "
            + $"{Count(ManagedFileVerificationStatus.Unchanged)} unchanged · {Count(ManagedFileVerificationStatus.Changed)} changed · "
            + $"{Count(ManagedFileVerificationStatus.Missing)} missing · {Count(ManagedFileVerificationStatus.Unreadable)} unreadable · "
            + $"{Count(ManagedFileVerificationStatus.NoBaseline)} without a baseline"
            + (WasCanceled ? $" · {Results.Count(result => !result.WasChecked)} not fully checked" : string.Empty);
}
