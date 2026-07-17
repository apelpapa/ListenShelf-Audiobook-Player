namespace ListenShelf.Desktop.Services;

public interface IFilePickerService
{
    Task<string?> PickM4bFileAsync();

    Task<IReadOnlyList<string>> PickM4bFilesAsync();
}
