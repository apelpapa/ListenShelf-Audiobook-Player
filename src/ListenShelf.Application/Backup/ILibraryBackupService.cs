namespace ListenShelf.Application.Backup;

public interface ILibraryBackupService
{
    string DataRootPath { get; }

    LibraryBackupSummary Export(
        string destinationPath,
        IProgress<LibraryBackupProgress>? progress = null);

    LibraryBackupSummary Inspect(
        string backupPath,
        IProgress<LibraryBackupProgress>? progress = null);

    LibraryRestoreResult Restore(
        string backupPath,
        IProgress<LibraryBackupProgress>? progress = null);
}
