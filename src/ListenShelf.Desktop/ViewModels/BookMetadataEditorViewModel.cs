using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ListenShelf.Application.Library;

namespace ListenShelf.Desktop.ViewModels;

public sealed partial class BookMetadataEditorViewModel : ViewModelBase, IDisposable
{
    private readonly IBookMetadataProvider _metadataProvider;
    private readonly bool _hasExistingCover;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private BookCoverImage? _selectedOnlineCover;
    private bool _disposed;

    public BookMetadataEditorViewModel(
        AudiobookMetadata metadata,
        AudiobookMetadataSuggestions suggestions,
        IBookMetadataProvider metadataProvider,
        bool hasExistingCover)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(suggestions);
        _metadataProvider = metadataProvider ?? throw new ArgumentNullException(nameof(metadataProvider));
        _hasExistingCover = hasExistingCover;

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
        _onlineSearchQuery = CreateInitialSearchQuery(metadata.Title);

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

    public ObservableCollection<OnlineBookSearchResultViewModel> OnlineSearchResults { get; } = [];

    public string MetadataProviderName => _metadataProvider.Name;

    public bool HasOnlineSearchResults => OnlineSearchResults.Count > 0;

    public bool HasOnlineSearchStatus => !string.IsNullOrWhiteSpace(OnlineSearchStatus);

    public bool HasOnlineCoverPreview => OnlineCoverPreview is not null;

    public bool CanSave => !IsOnlineLookupBusy;

    public string OnlineCoverOptionText => _hasExistingCover
        ? "Replace the current cover with this Open Library cover"
        : "Save this Open Library cover with the audiobook";

    public BookCoverImage? SelectedCoverToSave => SaveOnlineCover
        ? _selectedOnlineCover
        : null;

    [ObservableProperty]
    private string _onlineSearchQuery;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOnlineSearchStatus))]
    private string _onlineSearchStatus = "Search by title, author, ISBN, or the cleaned filename already filled in.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UseSelectedOnlineResultCommand))]
    private OnlineBookSearchResultViewModel? _selectedOnlineSearchResult;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyCanExecuteChangedFor(nameof(SearchOnlineCommand))]
    [NotifyCanExecuteChangedFor(nameof(UseSelectedOnlineResultCommand))]
    private bool _isOnlineLookupBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOnlineCoverPreview))]
    private Bitmap? _onlineCoverPreview;

    [ObservableProperty]
    private bool _saveOnlineCover;

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

    [RelayCommand(CanExecute = nameof(CanSearchOnline))]
    private async Task SearchOnlineAsync(CancellationToken cancellationToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);

        IsOnlineLookupBusy = true;
        SelectedOnlineSearchResult = null;
        OnlineSearchResults.Clear();
        OnPropertyChanged(nameof(HasOnlineSearchResults));
        OnlineSearchStatus = $"Searching {_metadataProvider.Name}…";

        try
        {
            var results = await _metadataProvider.SearchAsync(
                OnlineSearchQuery,
                linkedCancellation.Token);
            foreach (var result in results)
            {
                OnlineSearchResults.Add(new OnlineBookSearchResultViewModel(result));
            }

            OnPropertyChanged(nameof(HasOnlineSearchResults));
            OnlineSearchStatus = OnlineSearchResults.Count switch
            {
                0 => "No matching books were found. Try a shorter title, author, or ISBN.",
                1 => "One possible match found. Select it and review the details before saving.",
                _ => $"{OnlineSearchResults.Count} possible matches found. Select one and review it before saving.",
            };
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
            OnlineSearchStatus = "The Open Library request timed out. Please try again.";
        }
        catch (HttpRequestException exception)
        {
            OnlineSearchStatus = exception.StatusCode is { } statusCode
                ? $"Open Library returned {(int)statusCode}. Please try again shortly."
                : "Open Library could not be reached. Check the internet connection and try again.";
        }
        catch (JsonException)
        {
            OnlineSearchStatus = "Open Library returned data ListenShelf could not read.";
        }
        finally
        {
            IsOnlineLookupBusy = false;
        }
    }

    private bool CanSearchOnline() =>
        !IsOnlineLookupBusy && !string.IsNullOrWhiteSpace(OnlineSearchQuery);

    partial void OnOnlineSearchQueryChanged(string value) => SearchOnlineCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanUseSelectedOnlineResult))]
    private async Task UseSelectedOnlineResultAsync(CancellationToken cancellationToken)
    {
        if (SelectedOnlineSearchResult is not { } selectedResult)
        {
            return;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);
        IsOnlineLookupBusy = true;
        SetOnlineCover(null);

        var result = selectedResult.Result;
        ApplyMetadata(result);
        OnlineSearchStatus = result.CoverUri is null
            ? "Book details filled in. Review them below before saving."
            : "Book details filled in. Downloading the selected cover…";

        try
        {
            if (result.CoverUri is { } coverUri)
            {
                var cover = await _metadataProvider.DownloadCoverAsync(
                    coverUri,
                    linkedCancellation.Token);
                if (cover is not null)
                {
                    SetOnlineCover(cover);
                    SaveOnlineCover = !_hasExistingCover;
                    OnlineSearchStatus = _hasExistingCover
                        ? "Details filled in. A cover is available; opt in below to replace your current cover."
                        : "Details and cover are ready. Review everything below before saving.";
                }
                else
                {
                    OnlineSearchStatus = "Book details filled in, but no cover was available.";
                }
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
            OnlineSearchStatus = "Book details filled in, but the cover request timed out.";
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidDataException)
        {
            OnlineSearchStatus = "Book details filled in, but the cover could not be downloaded.";
        }
        finally
        {
            IsOnlineLookupBusy = false;
        }
    }

    private bool CanUseSelectedOnlineResult() =>
        !IsOnlineLookupBusy && SelectedOnlineSearchResult is not null;

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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        OnlineCoverPreview?.Dispose();
        OnlineCoverPreview = null;
    }

    private void ApplyMetadata(BookMetadataSearchResult result)
    {
        Title = result.Title;
        SetIfPresent(result.Subtitle, value => Subtitle = value);
        if (result.Authors.Count > 0)
        {
            ReplaceValues(Authors, result.Authors, RemoveAuthor);
        }

        SetIfPresent(result.SeriesName, value => SeriesName = value);
        SetIfPresent(result.SeriesPosition, value => SeriesPosition = value);
        if (result.OriginalPublicationYear is { } publicationYear)
        {
            OriginalPublicationYear = publicationYear;
        }

        SetIfPresent(result.Publisher, value => OriginalPublisher = value);
        SetIfPresent(result.Description, value => Description = value);
        if (result.Genres.Count > 0)
        {
            ReplaceValues(Genres, result.Genres, RemoveGenre);
        }

        SetIfPresent(result.Language, value => Language = value);
        SetIfPresent(result.Isbn10, value => Isbn10 = value);
        SetIfPresent(result.Isbn13, value => Isbn13 = value);
        ErrorMessage = string.Empty;
    }

    private void SetOnlineCover(BookCoverImage? cover)
    {
        OnlineCoverPreview?.Dispose();
        OnlineCoverPreview = null;
        _selectedOnlineCover = cover;
        SaveOnlineCover = false;

        if (cover is null)
        {
            return;
        }

        using var stream = new MemoryStream(cover.Bytes, writable: false);
        var preview = new Bitmap(stream);
        if (preview.PixelSize.Width <= 0 || preview.PixelSize.Height <= 0)
        {
            preview.Dispose();
            _selectedOnlineCover = null;
            throw new InvalidDataException("Open Library returned an unreadable cover image.");
        }

        OnlineCoverPreview = preview;
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

    private static void ReplaceValues(
        ObservableCollection<MetadataValueItemViewModel> destination,
        IEnumerable<string> values,
        Action<MetadataValueItemViewModel> remove)
    {
        destination.Clear();
        AddInitialValues(destination, values, remove);
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

    private static void SetIfPresent(string? value, Action<string> apply)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is not null)
        {
            apply(normalized);
        }
    }

    private void RemoveAuthor(MetadataValueItemViewModel author) => Authors.Remove(author);

    private void RemoveGenre(MetadataValueItemViewModel genre) => Genres.Remove(genre);

    private void RemoveNarrator(MetadataValueItemViewModel narrator) => Narrators.Remove(narrator);

    private static string CreateInitialSearchQuery(string title)
    {
        var withoutLeadingNumber = Regex.Replace(
            title,
            @"^\s*\d+\s*[-_.:)]+\s*",
            string.Empty,
            RegexOptions.CultureInvariant);
        return Regex.Replace(withoutLeadingNumber.Replace('_', ' '), @"\s+", " ").Trim();
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IEnumerable<string> NormalizeValues(IEnumerable<string> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);
}
