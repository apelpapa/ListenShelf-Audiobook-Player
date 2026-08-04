namespace ListenShelf.Desktop.Services;

public interface IFilePickerService
{
    Task<IReadOnlyList<string>> PickAudiobookFilesAsync();

    Task<string?> PickCoverImageAsync();

    Task<string?> PickBackupExportPathAsync(string suggestedFileName);

    Task<string?> PickBackupImportPathAsync();
}
