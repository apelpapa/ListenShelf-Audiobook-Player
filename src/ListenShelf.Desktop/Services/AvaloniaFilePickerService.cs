using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace ListenShelf.Desktop.Services;

public sealed class AvaloniaFilePickerService(Window owner) : IFilePickerService
{
    private static readonly FilePickerFileType M4bFileType = new("M4B audiobooks")
    {
        Patterns = ["*.m4b"],
    };

    private static readonly FilePickerFileType CoverImageFileType = new("Cover images")
    {
        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp"],
        MimeTypes = ["image/png", "image/jpeg", "image/webp"],
        AppleUniformTypeIdentifiers = ["public.png", "public.jpeg", "org.webmproject.webp"],
    };

    public async Task<string?> PickM4bFileAsync()
    {
        var files = await PickM4bFilesAsync(allowMultiple: false);
        return files.Count == 1 ? files[0] : null;
    }

    public Task<IReadOnlyList<string>> PickM4bFilesAsync() =>
        PickM4bFilesAsync(allowMultiple: true);

    public async Task<string?> PickCoverImageAsync()
    {
        if (!owner.StorageProvider.CanOpen)
        {
            throw new NotSupportedException("This system does not provide a file picker.");
        }

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose an audiobook cover",
            AllowMultiple = false,
            FileTypeFilter = [CoverImageFileType],
            SuggestedFileType = CoverImageFileType,
        });

        return files.Count == 1 ? files[0].Path.LocalPath : null;
    }

    private async Task<IReadOnlyList<string>> PickM4bFilesAsync(bool allowMultiple)
    {
        if (!owner.StorageProvider.CanOpen)
        {
            throw new NotSupportedException("This system does not provide a file picker.");
        }

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = allowMultiple ? "Add M4B audiobooks" : "Open an M4B audiobook",
            AllowMultiple = allowMultiple,
            FileTypeFilter = [M4bFileType],
            SuggestedFileType = M4bFileType,
        });

        return files.Select(file => file.Path.LocalPath).ToArray();
    }
}
