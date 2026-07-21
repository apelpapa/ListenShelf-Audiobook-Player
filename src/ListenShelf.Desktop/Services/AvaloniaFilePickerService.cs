using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ListenShelf.Application.Library;

namespace ListenShelf.Desktop.Services;

public sealed class AvaloniaFilePickerService(Window owner) : IFilePickerService
{
    private static readonly FilePickerFileType AudiobookFileType = new("Supported audiobooks")
    {
        Patterns = AudiobookFileFormats.SupportedExtensions
            .Select(extension => $"*{extension}")
            .ToArray(),
        MimeTypes = ["audio/mp4", "audio/mpeg"],
        AppleUniformTypeIdentifiers = ["public.mpeg-4-audio", "public.mp3"],
    };

    private static readonly FilePickerFileType CoverImageFileType = new("Cover images")
    {
        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp"],
        MimeTypes = ["image/png", "image/jpeg", "image/webp"],
        AppleUniformTypeIdentifiers = ["public.png", "public.jpeg", "org.webmproject.webp"],
    };

    public async Task<string?> PickAudiobookFileAsync()
    {
        var files = await PickAudiobookFilesAsync(allowMultiple: false);
        return files.Count == 1 ? files[0] : null;
    }

    public Task<IReadOnlyList<string>> PickAudiobookFilesAsync() =>
        PickAudiobookFilesAsync(allowMultiple: true);

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

    private async Task<IReadOnlyList<string>> PickAudiobookFilesAsync(bool allowMultiple)
    {
        if (!owner.StorageProvider.CanOpen)
        {
            throw new NotSupportedException("This system does not provide a file picker.");
        }

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = allowMultiple ? "Add audiobooks" : "Open an audiobook",
            AllowMultiple = allowMultiple,
            FileTypeFilter = [AudiobookFileType],
            SuggestedFileType = AudiobookFileType,
        });

        return files.Select(file => file.Path.LocalPath).ToArray();
    }
}
