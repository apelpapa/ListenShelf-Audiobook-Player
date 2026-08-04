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

    private static readonly FilePickerFileType ListenShelfBackupFileType =
        new("ListenShelf backups")
        {
            Patterns = ["*.listenshelf-backup"],
            MimeTypes = ["application/zip"],
            AppleUniformTypeIdentifiers = ["public.zip-archive"],
        };

    public async Task<IReadOnlyList<string>> PickAudiobookFilesAsync()
    {
        if (!owner.StorageProvider.CanOpen)
        {
            throw new NotSupportedException("This system does not provide a file picker.");
        }

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add audiobooks",
            AllowMultiple = true,
            FileTypeFilter = [AudiobookFileType],
            SuggestedFileType = AudiobookFileType,
        });

        return files.Select(file => file.Path.LocalPath).ToArray();
    }

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

    public async Task<string?> PickBackupExportPathAsync(string suggestedFileName)
    {
        if (!owner.StorageProvider.CanSave)
        {
            throw new NotSupportedException("This system does not provide a save-file picker.");
        }

        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export ListenShelf backup",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "listenshelf-backup",
            FileTypeChoices = [ListenShelfBackupFileType],
            SuggestedFileType = ListenShelfBackupFileType,
        });

        return file?.Path.LocalPath;
    }

    public async Task<string?> PickBackupImportPathAsync()
    {
        if (!owner.StorageProvider.CanOpen)
        {
            throw new NotSupportedException("This system does not provide a file picker.");
        }

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a ListenShelf backup to restore",
            AllowMultiple = false,
            FileTypeFilter = [ListenShelfBackupFileType],
            SuggestedFileType = ListenShelfBackupFileType,
        });

        return files.Count == 1 ? files[0].Path.LocalPath : null;
    }

}
