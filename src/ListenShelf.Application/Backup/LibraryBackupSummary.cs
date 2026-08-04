namespace ListenShelf.Application.Backup;

public sealed record LibraryBackupSummary(
    string BackupPath,
    DateTimeOffset CreatedAtUtc,
    int BookCount,
    int FileCount,
    long UncompressedSizeBytes,
    long ArchiveSizeBytes,
    int FormatVersion,
    bool IsComplete);
