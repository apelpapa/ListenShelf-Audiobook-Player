namespace ListenShelf.Application.Library;

public interface IManagedFileRepairer
{
    ManagedFileRepairResult Repair(Guid bookId, string sourceFilePath,
        IProgress<LibraryImportProgress>? progress = null, CancellationToken cancellationToken = default);
}

public sealed record ManagedFileRepairResult(Guid BookId, string Title, string FilePath, string? RetainedCopyPath);
