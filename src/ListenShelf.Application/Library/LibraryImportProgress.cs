namespace ListenShelf.Application.Library;

public enum LibraryImportStage
{
    Checking,
    Copying,
    Verifying,
    Finalizing,
}

public sealed record LibraryImportProgress(
    LibraryImportStage Stage,
    long ProcessedBytes = 0,
    long TotalBytes = 0)
{
    public double StageFraction => TotalBytes > 0
        ? Math.Clamp((double)ProcessedBytes / TotalBytes, 0d, 1d)
        : 0d;

    // Copying and rereading for verification each account for half of a file.
    public double FileFraction => Stage switch
    {
        LibraryImportStage.Copying => StageFraction * 0.5d,
        LibraryImportStage.Verifying => 0.5d + StageFraction * 0.5d,
        LibraryImportStage.Finalizing => 1d,
        _ => 0d,
    };
}
