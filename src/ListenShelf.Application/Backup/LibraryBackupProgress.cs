namespace ListenShelf.Application.Backup;

public sealed record LibraryBackupProgress(
    string Stage,
    int CompletedFiles,
    int TotalFiles,
    long CompletedBytes,
    long TotalBytes)
{
    public double Percentage => TotalBytes <= 0
        ? 0d
        : Math.Clamp(CompletedBytes * 100d / TotalBytes, 0d, 100d);
}
