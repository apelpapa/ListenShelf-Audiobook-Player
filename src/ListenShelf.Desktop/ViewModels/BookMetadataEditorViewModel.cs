using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ListenShelf.Application.Library;

namespace ListenShelf.Desktop.ViewModels;

public sealed partial class BookMetadataEditorViewModel : ViewModelBase
{
    public BookMetadataEditorViewModel(
        AudiobookMetadata metadata,
        AudiobookMetadataSuggestions suggestions)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(suggestions);

        _title = metadata.Title;
        _subtitle = metadata.Subtitle ?? string.Empty;
        _seriesName = metadata.SeriesName ?? string.Empty;
        _seriesPosition = metadata.SeriesPosition ?? string.Empty;
        _originalPublicationYear = metadata.OriginalPublicationYear;
        _originalPublisher = metadata.OriginalPublisher ?? string.Empty;
        _description = metadata.Description ?? string.Empty;
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

        AuthorSuggestions = suggestions.Authors;
        SeriesSuggestions = suggestions.SeriesNames;
        OriginalPublisherSuggestions = suggestions.OriginalPublishers;
        GenreSuggestions = suggestions.Genres;
        NarratorSuggestions = suggestions.Narrators;
        AudioPublisherSuggestions = suggestions.AudioPublishers;
        LanguageSuggestions = suggestions.Languages;

        AddInitialValues(Authors, metadata.Authors, RemoveAuthor);
        AddInitialValues(Genres, metadata.Genres, RemoveGenre);
        AddInitialValues(Narrators, metadata.Narrators, RemoveNarrator);
    }

    public IReadOnlyList<AudiobookAbridgement> AbridgementOptions { get; } =
        Enum.GetValues<AudiobookAbridgement>();

    public IReadOnlyList<string> AuthorSuggestions { get; }

    public IReadOnlyList<string> SeriesSuggestions { get; }

    public IReadOnlyList<string> OriginalPublisherSuggestions { get; }

    public IReadOnlyList<string> GenreSuggestions { get; }

    public IReadOnlyList<string> NarratorSuggestions { get; }

    public IReadOnlyList<string> AudioPublisherSuggestions { get; }

    public IReadOnlyList<string> LanguageSuggestions { get; }

    public ObservableCollection<MetadataValueItemViewModel> Authors { get; } = [];

    public ObservableCollection<MetadataValueItemViewModel> Genres { get; } = [];

    public ObservableCollection<MetadataValueItemViewModel> Narrators { get; } = [];

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string _subtitle;

    [ObservableProperty]
    private string _authorInput = string.Empty;

    [ObservableProperty]
    private string _seriesName;

    [ObservableProperty]
    private string _seriesPosition;

    [ObservableProperty]
    private decimal? _originalPublicationYear;

    [ObservableProperty]
    private string _originalPublisher;

    [ObservableProperty]
    private string _description;

    [ObservableProperty]
    private string _genreInput = string.Empty;

    [ObservableProperty]
    private string _narratorInput = string.Empty;

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

    [RelayCommand]
    private void AddAuthor() =>
        AddValue(Authors, AuthorInput, value => AuthorInput = value, RemoveAuthor);

    [RelayCommand]
    private void AddGenre() =>
        AddValue(Genres, GenreInput, value => GenreInput = value, RemoveGenre);

    [RelayCommand]
    private void AddNarrator() =>
        AddValue(Narrators, NarratorInput, value => NarratorInput = value, RemoveNarrator);

    public bool TryCreateMetadata(out AudiobookMetadata? metadata)
    {
        var normalizedTitle = NormalizeOptional(Title);
        if (normalizedTitle is null)
        {
            ErrorMessage = "Title is required.";
            metadata = null;
            return false;
        }

        AddPendingValues();

        int? publicationYear = null;
        if (OriginalPublicationYear is { } year)
        {
            if (year != decimal.Truncate(year) || year is < 1000 or > 9999)
            {
                ErrorMessage = "Original publication year must be a four-digit year.";
                metadata = null;
                return false;
            }

            publicationYear = decimal.ToInt32(year);
        }

        metadata = new AudiobookMetadata
        {
            Title = normalizedTitle,
            Subtitle = NormalizeOptional(Subtitle),
            Authors = Authors.Select(item => item.Value).ToArray(),
            SeriesName = NormalizeOptional(SeriesName),
            SeriesPosition = NormalizeOptional(SeriesPosition),
            OriginalPublicationYear = publicationYear,
            OriginalPublisher = NormalizeOptional(OriginalPublisher),
            Description = NormalizeOptional(Description),
            Genres = Genres.Select(item => item.Value).ToArray(),
            Narrators = Narrators.Select(item => item.Value).ToArray(),
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

    private void AddPendingValues()
    {
        AddAuthor();
        AddGenre();
        AddNarrator();
    }

    private static void AddInitialValues(
        ObservableCollection<MetadataValueItemViewModel> destination,
        IEnumerable<string> values,
        Action<MetadataValueItemViewModel> remove)
    {
        foreach (var value in NormalizeValues(values))
        {
            destination.Add(new MetadataValueItemViewModel(value, remove));
        }
    }

    private static void AddValue(
        ObservableCollection<MetadataValueItemViewModel> destination,
        string input,
        Action<string> setInput,
        Action<MetadataValueItemViewModel> remove)
    {
        var normalized = NormalizeOptional(input);
        if (normalized is null)
        {
            return;
        }

        if (!destination.Any(item =>
                string.Equals(item.Value, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            destination.Add(new MetadataValueItemViewModel(normalized, remove));
        }

        setInput(string.Empty);
    }

    private void RemoveAuthor(MetadataValueItemViewModel author) => Authors.Remove(author);

    private void RemoveGenre(MetadataValueItemViewModel genre) => Genres.Remove(genre);

    private void RemoveNarrator(MetadataValueItemViewModel narrator) => Narrators.Remove(narrator);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IEnumerable<string> NormalizeValues(IEnumerable<string> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);
}
