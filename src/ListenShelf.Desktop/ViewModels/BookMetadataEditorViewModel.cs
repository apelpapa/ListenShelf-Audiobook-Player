using CommunityToolkit.Mvvm.ComponentModel;
using ListenShelf.Application.Library;

namespace ListenShelf.Desktop.ViewModels;

public sealed partial class BookMetadataEditorViewModel : ViewModelBase
{
    public BookMetadataEditorViewModel(AudiobookMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        _title = metadata.Title;
        _subtitle = metadata.Subtitle ?? string.Empty;
        _authors = JoinValues(metadata.Authors);
        _seriesName = metadata.SeriesName ?? string.Empty;
        _seriesPosition = metadata.SeriesPosition ?? string.Empty;
        _originalPublicationYear = metadata.OriginalPublicationYear?.ToString() ?? string.Empty;
        _originalPublisher = metadata.OriginalPublisher ?? string.Empty;
        _description = metadata.Description ?? string.Empty;
        _genres = JoinValues(metadata.Genres);
        _narrators = JoinValues(metadata.Narrators);
        _audioPublisher = metadata.AudioPublisher ?? string.Empty;
        _audiobookReleaseDate = metadata.AudiobookReleaseDate is { } releaseDate
            ? new DateTimeOffset(releaseDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : null;
        _language = metadata.Language ?? string.Empty;
        _isbn10 = metadata.Isbn10 ?? string.Empty;
        _isbn13 = metadata.Isbn13 ?? string.Empty;
        _asin = metadata.Asin ?? string.Empty;
        _editionName = metadata.EditionName ?? string.Empty;
        _selectedAbridgement = metadata.Abridgement;
        _editionNotes = metadata.EditionNotes ?? string.Empty;
    }

    public IReadOnlyList<AudiobookAbridgement> AbridgementOptions { get; } =
        Enum.GetValues<AudiobookAbridgement>();

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string _subtitle;

    [ObservableProperty]
    private string _authors;

    [ObservableProperty]
    private string _seriesName;

    [ObservableProperty]
    private string _seriesPosition;

    [ObservableProperty]
    private string _originalPublicationYear;

    [ObservableProperty]
    private string _originalPublisher;

    [ObservableProperty]
    private string _description;

    [ObservableProperty]
    private string _genres;

    [ObservableProperty]
    private string _narrators;

    [ObservableProperty]
    private string _audioPublisher;

    [ObservableProperty]
    private DateTimeOffset? _audiobookReleaseDate;

    [ObservableProperty]
    private string _language;

    [ObservableProperty]
    private string _isbn10;

    [ObservableProperty]
    private string _isbn13;

    [ObservableProperty]
    private string _asin;

    [ObservableProperty]
    private string _editionName;

    [ObservableProperty]
    private AudiobookAbridgement _selectedAbridgement;

    [ObservableProperty]
    private string _editionNotes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = string.Empty;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool TryCreateMetadata(out AudiobookMetadata? metadata)
    {
        var normalizedTitle = NormalizeOptional(Title);
        if (normalizedTitle is null)
        {
            ErrorMessage = "Title is required.";
            metadata = null;
            return false;
        }

        int? publicationYear = null;
        if (!string.IsNullOrWhiteSpace(OriginalPublicationYear))
        {
            if (!int.TryParse(OriginalPublicationYear.Trim(), out var parsedYear)
                || parsedYear is < 1 or > 9999)
            {
                ErrorMessage = "Original publication year must be between 1 and 9999.";
                metadata = null;
                return false;
            }

            publicationYear = parsedYear;
        }

        metadata = new AudiobookMetadata
        {
            Title = normalizedTitle,
            Subtitle = NormalizeOptional(Subtitle),
            Authors = ParseValues(Authors),
            SeriesName = NormalizeOptional(SeriesName),
            SeriesPosition = NormalizeOptional(SeriesPosition),
            OriginalPublicationYear = publicationYear,
            OriginalPublisher = NormalizeOptional(OriginalPublisher),
            Description = NormalizeOptional(Description),
            Genres = ParseValues(Genres),
            Narrators = ParseValues(Narrators),
            AudioPublisher = NormalizeOptional(AudioPublisher),
            AudiobookReleaseDate = AudiobookReleaseDate is { } releaseDate
                ? DateOnly.FromDateTime(releaseDate.Date)
                : null,
            Language = NormalizeOptional(Language),
            Isbn10 = NormalizeOptional(Isbn10),
            Isbn13 = NormalizeOptional(Isbn13),
            Asin = NormalizeOptional(Asin),
            EditionName = NormalizeOptional(EditionName),
            Abridgement = SelectedAbridgement,
            EditionNotes = NormalizeOptional(EditionNotes),
        };
        ErrorMessage = string.Empty;
        return true;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> ParseValues(string values) =>
        values
            .Split([';', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string JoinValues(IEnumerable<string> values) =>
        string.Join("; ", values);
}
