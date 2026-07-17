using CommunityToolkit.Mvvm.Input;
using ListenShelf.Application.Library;

namespace ListenShelf.Desktop.ViewModels;

public partial class LibraryBookItemViewModel(
    LibraryBook book,
    string progressSummary,
    Func<LibraryBook, Task> playBookAsync) : ViewModelBase
{
    public LibraryBook Book { get; } = book;

    public string Title => Book.Title;

    public string FileName => Path.GetFileName(Book.FilePath);

    public string FilePath => Book.FilePath;

    public string StorageModeText => Book.StorageMode == LibraryStorageMode.Linked
        ? "LINKED"
        : "MANAGED";

    public string FileSizeText => FormatFileSize(Book.FileSizeBytes);

    public string ProgressSummary { get; } = progressSummary;

    public bool IsAvailable => File.Exists(Book.FilePath);

    public string AvailabilityText => IsAvailable ? "Ready" : "Missing file";

    [RelayCommand(CanExecute = nameof(IsAvailable))]
    private Task PlayAsync() => playBookAsync(Book);

    private static string FormatFileSize(long bytes)
    {
        const double megabyte = 1024d * 1024d;
        const double gigabyte = megabyte * 1024d;

        return bytes >= gigabyte
            ? $"{bytes / gigabyte:0.##} GB"
            : $"{bytes / megabyte:0.#} MB";
    }
}
