using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using ListenShelf.Application.Library;

namespace ListenShelf.Desktop.ViewModels;

public sealed partial class LibraryBookItemViewModel : ViewModelBase, IDisposable
{
    private readonly Func<LibraryBook, Task> _playBookAsync;
    private readonly Func<LibraryBook, Task> _chooseCoverAsync;

    public LibraryBookItemViewModel(
        LibraryBook book,
        string progressSummary,
        Func<LibraryBook, Task> playBookAsync,
        Func<LibraryBook, Task> chooseCoverAsync)
    {
        Book = book;
        ProgressSummary = progressSummary;
        _playBookAsync = playBookAsync;
        _chooseCoverAsync = chooseCoverAsync;
        CoverImage = TryLoadCover(book.CoverPath);
    }

    public LibraryBook Book { get; }

    public Bitmap? CoverImage { get; }

    public bool HasCover => CoverImage is not null;

    public bool HasNoCover => !HasCover;

    public string CoverButtonText => HasCover ? "Change cover" : "Add cover";

    public string Title => Book.Title;

    public string FileName => Path.GetFileName(Book.FilePath);

    public string FilePath => Book.FilePath;

    public string StorageModeText => Book.StorageMode == LibraryStorageMode.Linked
        ? "LINKED"
        : "MANAGED";

    public string FileSizeText => FormatFileSize(Book.FileSizeBytes);

    public string ProgressSummary { get; }

    public bool IsAvailable => File.Exists(Book.FilePath);

    public string AvailabilityText => IsAvailable ? "Ready" : "Missing file";

    [RelayCommand(CanExecute = nameof(IsAvailable))]
    private Task PlayAsync() => _playBookAsync(Book);

    [RelayCommand]
    private Task ChooseCoverAsync() => _chooseCoverAsync(Book);

    public void Dispose() => CoverImage?.Dispose();

    private static Bitmap? TryLoadCover(string? coverPath)
    {
        if (string.IsNullOrWhiteSpace(coverPath) || !File.Exists(coverPath))
        {
            return null;
        }

        try
        {
            return new Bitmap(coverPath);
        }
        catch
        {
            return null;
        }
    }

    private static string FormatFileSize(long bytes)
    {
        const double megabyte = 1024d * 1024d;
        const double gigabyte = megabyte * 1024d;

        return bytes >= gigabyte
            ? $"{bytes / gigabyte:0.##} GB"
            : $"{bytes / megabyte:0.#} MB";
    }
}
