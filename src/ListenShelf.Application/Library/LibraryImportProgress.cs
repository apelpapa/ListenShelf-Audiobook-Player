namespace ListenShelf.Application.Library;

public enum LibraryImportStage
{
    Checking,
    Fingerprinting,
    Comparing,
    Copying,
    Verifying,
    Finalizing,
}

public sealed record LibraryImportProgress(
    LibraryImportStage Stage,
    long ProcessedBytes = 0,
    long TotalBytes = 0,
    string? Detail = null)
{
    public double StageFraction => TotalBytes > 0
        ? Math.Clamp((double)ProcessedBytes / TotalBytes, 0d, 1d)
        : 0d;

    // Work estimate, not elapsed time. Duplicate checks are skipped when no
    // same-sized books exist; comparing several older books can take longer.
    public double FileFraction => Stage switch
    {
        LibraryImportStage.Fingerprinting => StageFraction * 0.25d,
        LibraryImportStage.Comparing => 0.25d,
        LibraryImportStage.Copying => 0.25d + StageFraction * 0.375d,
        LibraryImportStage.Verifying => 0.625d + StageFraction * 0.375d,
        LibraryImportStage.Finalizing => 1d,
        _ => 0d,
    };
}
