using ListenShelf.Application.Backup;

namespace ListenShelf.Infrastructure.Backup;

public sealed record DatabaseRecoveryRestoreResult(
    LibraryBackupSummary RestoredBackup,
    string? PreservedDataPath);
