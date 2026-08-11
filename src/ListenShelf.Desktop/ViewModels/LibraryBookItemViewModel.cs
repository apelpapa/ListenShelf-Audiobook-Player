using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ListenShelf.Application.Library;
using ListenShelf.Application.Progress;
using ListenShelf.Application.Settings;

namespace ListenShelf.Desktop.ViewModels;

public sealed partial class LibraryBookItemViewModel : ViewModelBase, IDisposable
{
    private readonly Func<LibraryBook, Task> _playBookAsync;
    private readonly Func<LibraryBook, Task> _chooseCoverAsync;
    private readonly Func<LibraryBook, Task> _editMetadataAsync;
    private readonly Func<LibraryBook, Task> _removeBookAsync;

    public LibraryBookItemViewModel(
        LibraryBook book,
        PlaybackProgress? progress,
        double tileWidth,
        Func<LibraryBook, Task> playBookAsync,
        Func<LibraryBook, Task> chooseCoverAsync,
        Func<LibraryBook, Task> editMetadataAsync,
        Func<LibraryBook, Task> removeBookAsync,
        bool isProgressAvailable = true)
    {
        Book = book;
        Progress = progress;
        IsProgressAvailable = isProgressAvailable;
        _playBookAsync = playBookAsync;
        _chooseCoverAsync = chooseCoverAsync;
        _editMetadataAsync = editMetadataAsync;
        _removeBookAsync = removeBookAsync;
        CoverImage = TryLoadCover(book.CoverPath);
        SetTileWidth(tileWidth);
    }

    public LibraryBook Book { get; }

    public Bitmap? CoverImage { get; }

    public bool HasCover => CoverImage is not null;

    public bool HasNoCover => !HasCover;

    public string CoverButtonText => HasCover ? "Change cover" : "Add cover";

    public string Title => Book.Title;

    public string AuthorText => Book.Metadata.Authors.Count > 0
        ? string.Join(", ", Book.Metadata.Authors)
        : "Unknown author";

    public bool HasSeries => !string.IsNullOrWhiteSpace(Book.Metadata.SeriesName);

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

    public string FileSizeText => FormatFileSize(Book.FileSizeBytes);

    public PlaybackProgress? Progress { get; private set; }

    public bool IsProgressAvailable { get; private set; }

    public string ProgressSummary
    {
        get
        {
            if (!IsAvailable)
            {
                return "File missing";
            }

            if (!IsProgressAvailable)
            {
                return "Progress unavailable";
            }

            var status = LibraryBookQuery.GetStatus(Progress);
            if (status == LibraryStatusFilter.Finished)
            {
                return "Finished";
            }

            if (status == LibraryStatusFilter.NotStarted)
            {
                return "Not started";
            }

            var position = Progress!.Position;
            var timestamp = Progress.Duration.TotalHours >= 1 || position.TotalHours >= 1
                ? $"{(int)position.TotalHours}:{position.Minutes:00}:{position.Seconds:00}"
                : $"{position.Minutes}:{position.Seconds:00}";
            return $"In progress · Resume at {timestamp}";
        }
    }

    public void UpdateProgress(PlaybackProgress progress)
    {
        Progress = progress;
        IsProgressAvailable = true;
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(IsProgressAvailable));
        OnPropertyChanged(nameof(ProgressSummary));
    }

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

    [RelayCommand]
    private Task ChooseCoverAsync() => _chooseCoverAsync(Book);

    [RelayCommand]
    private Task EditMetadataAsync() => _editMetadataAsync(Book);

    [RelayCommand]
    private Task RemoveBookAsync() => _removeBookAsync(Book);

    public void SetTileWidth(double tileWidth)
    {
        TileWidth = tileWidth;
        TileArtworkWidth = Math.Max(140d, tileWidth - 40d);
        TileArtworkHeight = TileArtworkWidth * 1.5d;
        TileHeight = TileArtworkHeight + 313d;
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
