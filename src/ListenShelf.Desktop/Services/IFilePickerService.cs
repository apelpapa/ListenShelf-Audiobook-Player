namespace ListenShelf.Desktop.Services;

public interface IFilePickerService
{
    Task<IReadOnlyList<string>> PickAudiobookFilesAsync();

    Task<string?> PickCoverImageAsync();
}
