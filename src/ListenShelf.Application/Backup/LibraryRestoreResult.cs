namespace ListenShelf.Application.Backup;

public sealed record LibraryRestoreResult(
    LibraryBackupSummary RestoredBackup,
    string SafetyBackupPath,
    bool RollbackCleanupPending);
