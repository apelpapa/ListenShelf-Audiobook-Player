using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace ListenShelf.Desktop.Services;

public sealed class AvaloniaFilePickerService(Window owner) : IFilePickerService
{
    private static readonly FilePickerFileType M4bFileType = new("M4B audiobooks")
    {
        Patterns = ["*.m4b"],
    };

    public async Task<string?> PickM4bFileAsync()
    {
        if (!owner.StorageProvider.CanOpen)
        {
            throw new NotSupportedException("This system does not provide a file picker.");
        }

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open an M4B audiobook",
            AllowMultiple = false,
            FileTypeFilter = [M4bFileType],
            SuggestedFileType = M4bFileType,
        });

        return files.Count == 1 ? files[0].Path.LocalPath : null;
    }
}
