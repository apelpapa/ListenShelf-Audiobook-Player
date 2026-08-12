namespace ListenShelf.Application.Library;

public enum LibraryImportOutcome
{
    Added,
    AlreadyInLibrary,
    Failed,
    Canceled,
    NotProcessed,
}

public sealed record LibraryImportFileResult(string FilePath, LibraryImportOutcome Outcome, string Message);

public sealed record LibraryImportBatchProgress(
    int FileNumber,
    int TotalFiles,
    string FilePath,
    LibraryImportProgress FileProgress,
    LibraryImportFileResult? CompletedFile = null)
{
    public double OverallPercentage => TotalFiles == 0 ? 0d
        : 100d * (FileNumber - 1 + (CompletedFile is null ? FileProgress.FileFraction : 1d)) / TotalFiles;
}

public sealed record LibraryImportBatchResult(IReadOnlyList<LibraryImportFileResult> Files)
{
    public int AddedCount => Count(LibraryImportOutcome.Added);
    public int ExistingCount => Count(LibraryImportOutcome.AlreadyInLibrary);
    public int FailedCount => Count(LibraryImportOutcome.Failed);
    public int CanceledCount => Count(LibraryImportOutcome.Canceled);
    public int NotProcessedCount => Count(LibraryImportOutcome.NotProcessed);
    public bool WasCanceled => CanceledCount > 0 || NotProcessedCount > 0;

    public string Summary => $"{(WasCanceled ? "Import canceled" : "Import complete")}: "
        + $"{AddedCount} added · {ExistingCount} already in library · {FailedCount} failed"
        + (WasCanceled ? $" · {CanceledCount} canceled · {NotProcessedCount} not processed" : string.Empty);

    private int Count(LibraryImportOutcome outcome) => Files.Count(file => file.Outcome == outcome);
}

public sealed class LibraryImportBatch(IAudiobookLibrary library)
{
    public LibraryImportBatchResult Run(
        IReadOnlyList<string> filePaths,
        IProgress<LibraryImportBatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<LibraryImportFileResult>();
        for (var index = 0; index < filePaths.Count; index++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var filePath = filePaths[index];
            var fileNumber = index + 1;
            LibraryImportFileResult fileResult;
            try
            {
                var reporter = new CallbackProgress(update => progress?.Report(
                    new LibraryImportBatchProgress(fileNumber, filePaths.Count, filePath, update)));
                var result = library.Import(filePath, reporter, cancellationToken);
                fileResult = new LibraryImportFileResult(filePath,
                    result.WasAdded ? LibraryImportOutcome.Added : LibraryImportOutcome.AlreadyInLibrary,
                    result.WasAdded ? "Copied, verified, and added to the library."
                        : "This source location is already in the library; no extra copy was made.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                fileResult = new LibraryImportFileResult(filePath, LibraryImportOutcome.Canceled,
                    "Canceled. Any unfinished copy from this import was removed.");
            }
            catch (Exception exception)
            {
                fileResult = new LibraryImportFileResult(filePath, LibraryImportOutcome.Failed, exception.Message);
            }

            results.Add(fileResult);
            progress?.Report(new LibraryImportBatchProgress(fileNumber, filePaths.Count, filePath,
                new LibraryImportProgress(LibraryImportStage.Finalizing), fileResult));
        }

        for (var index = results.Count; index < filePaths.Count; index++)
        {
            results.Add(new LibraryImportFileResult(filePaths[index], LibraryImportOutcome.NotProcessed,
                "Not started because the batch was canceled."));
        }

        return new LibraryImportBatchResult(results.ToArray());
    }

    private sealed class CallbackProgress(Action<LibraryImportProgress> report) : IProgress<LibraryImportProgress>
    {
        public void Report(LibraryImportProgress value) => report(value);
    }
}
