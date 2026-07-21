using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ListenShelf.Application.Library;

namespace ListenShelf.Desktop.ViewModels;

public sealed partial class LibraryBookItemViewModel : ViewModelBase, IDisposable
{
    private readonly Func<LibraryBook, Task> _playBookAsync;
    private readonly Func<LibraryBook, Task> _chooseCoverAsync;
    private readonly Func<LibraryBook, Task> _editMetadataAsync;

    public LibraryBookItemViewModel(
        LibraryBook book,
        string progressSummary,
        double tileWidth,
        Func<LibraryBook, Task> playBookAsync,
        Func<LibraryBook, Task> chooseCoverAsync,
        Func<LibraryBook, Task> editMetadataAsync)
    {
        Book = book;
        ProgressSummary = progressSummary;
        _playBookAsync = playBookAsync;
        _chooseCoverAsync = chooseCoverAsync;
        _editMetadataAsync = editMetadataAsync;
        CoverImage = TryLoadCover(book.CoverPath);
        SetTileWidth(tileWidth);
    }

    public LibraryBook Book { get; }

    public Bitmap? CoverImage { get; }

    public bool HasCover => CoverImage is not null;

    public bool HasNoCover => !HasCover;

    public string CoverButtonText => HasCover ? "Change cover" : "Add cover";

    public string Title => Book.Title;

    public bool CanManageMetadata => Book.StorageMode == LibraryStorageMode.Managed;

    public string AuthorText => Book.Metadata.Authors.Count > 0
        ? string.Join(", ", Book.Metadata.Authors)
        : "Unknown author";

    public bool HasSeries =>
        CanManageMetadata && !string.IsNullOrWhiteSpace(Book.Metadata.SeriesName);

    public string SeriesText
    {
        get
        {
            if (!HasSeries)
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(Book.Metadata.SeriesPosition)
                ? Book.Metadata.SeriesName!
                : $"{Book.Metadata.SeriesName} · Book {Book.Metadata.SeriesPosition}";
        }
    }

    public bool HasDetailedMetadata =>
        Book.Metadata.Authors.Count > 0
        || !string.IsNullOrWhiteSpace(Book.Metadata.Subtitle)
        || HasSeries
        || Book.Metadata.OriginalPublicationYear is not null
        || Book.Metadata.Narrators.Count > 0
        || !string.IsNullOrWhiteSpace(Book.Metadata.AudioPublisher);

    public string DetailsButtonText => HasDetailedMetadata ? "Edit details" : "Add details";

    public string FileName => Path.GetFileName(Book.FilePath);

    public string FilePath => Book.FilePath;

    public string StorageModeText => Book.StorageMode == LibraryStorageMode.Linked
        ? "PLAYER ONLY"
        : "MANAGED";

    public string FileSizeText => FormatFileSize(Book.FileSizeBytes);

    public string ProgressSummary { get; }

    [ObservableProperty]
    private double _tileWidth;

    [ObservableProperty]
    private double _tileArtworkWidth;

    [ObservableProperty]
    private double _tileArtworkHeight;

    [ObservableProperty]
    private double _tileHeight;

    [ObservableProperty]
    private double _groupStackArtworkWidth;

    [ObservableProperty]
    private double _groupStackArtworkHeight;

    [ObservableProperty]
    private double _groupStackHeight;

    [ObservableProperty]
    private double _groupStackTileHeight;

    public bool IsAvailable => File.Exists(Book.FilePath);

    public string AvailabilityText => IsAvailable ? "Ready" : "Missing file";

    [RelayCommand(CanExecute = nameof(IsAvailable))]
    private Task PlayAsync() => _playBookAsync(Book);

    [RelayCommand(CanExecute = nameof(CanManageMetadata))]
    private Task ChooseCoverAsync() => _chooseCoverAsync(Book);

    [RelayCommand(CanExecute = nameof(CanManageMetadata))]
    private Task EditMetadataAsync() => _editMetadataAsync(Book);

    public void SetTileWidth(double tileWidth)
    {
        TileWidth = tileWidth;
        TileArtworkWidth = Math.Max(140d, tileWidth - 40d);
        TileArtworkHeight = TileArtworkWidth * 1.5d;
        TileHeight = TileArtworkHeight + 266d;
        GroupStackArtworkWidth = Math.Max(120d, tileWidth - 60d);
        GroupStackArtworkHeight = GroupStackArtworkWidth * 1.5d;
        GroupStackHeight = GroupStackArtworkHeight + 14d;
        GroupStackTileHeight = GroupStackHeight + 105d;
    }

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
