namespace ListenShelf.Desktop.Services;

public interface IFilePickerService
{
    Task<string?> PickAudiobookFileAsync();

    Task<IReadOnlyList<string>> PickAudiobookFilesAsync();

    Task<string?> PickCoverImageAsync();
}
