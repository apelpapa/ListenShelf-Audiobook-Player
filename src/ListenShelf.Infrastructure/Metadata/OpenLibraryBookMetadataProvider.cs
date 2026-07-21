using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ListenShelf.Application.Library;

namespace ListenShelf.Infrastructure.Metadata;

public sealed partial class OpenLibraryBookMetadataProvider : IBookMetadataProvider
{
    private const int SearchResultLimit = 10;
    private const int MaximumCoverBytes = 12 * 1024 * 1024;
    private static readonly TimeSpan SearchCacheDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MinimumRequestInterval = TimeSpan.FromSeconds(1);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyDictionary<string, string> LanguageNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ara"] = "Arabic",
            ["chi"] = "Chinese",
            ["cze"] = "Czech",
            ["dan"] = "Danish",
            ["dut"] = "Dutch",
            ["eng"] = "English",
            ["fin"] = "Finnish",
            ["fre"] = "French",
            ["ger"] = "German",
            ["hin"] = "Hindi",
            ["ita"] = "Italian",
            ["jpn"] = "Japanese",
            ["kor"] = "Korean",
            ["nor"] = "Norwegian",
            ["pol"] = "Polish",
            ["por"] = "Portuguese",
            ["rus"] = "Russian",
            ["spa"] = "Spanish",
            ["swe"] = "Swedish",
        };

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly Dictionary<string, SearchCacheEntry> _searchCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Uri, BookCoverImage> _coverCache = [];
    private DateTimeOffset _lastRequestStartedAtUtc = DateTimeOffset.MinValue;

    public OpenLibraryBookMetadataProvider()
        : this(CreateHttpClient())
    {
    }

    internal OpenLibraryBookMetadataProvider(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public string Name => "Open Library";

    public async Task<IReadOnlyList<BookMetadataSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = NormalizeSearchQuery(query);
        if (normalizedQuery.Length == 0)
        {
            return [];
        }

        if (_searchCache.TryGetValue(normalizedQuery, out var cached)
            && DateTimeOffset.UtcNow - cached.CreatedAtUtc < SearchCacheDuration)
        {
            return cached.Results;
        }

        var fields = string.Join(',',
        [
            "key",
            "title",
            "subtitle",
            "author_name",
            "first_publish_year",
            "publisher",
            "isbn",
            "language",
            "subject",
            "series",
            "cover_i",
            "first_sentence",
            "editions",
            "editions.key",
            "editions.title",
            "editions.subtitle",
            "editions.publisher",
            "editions.language",
            "editions.isbn",
            "editions.cover_i",
            "editions.series",
        ]);
        var requestUri = new Uri(
            $"search.json?q={Uri.EscapeDataString(normalizedQuery)}"
            + $"&fields={Uri.EscapeDataString(fields)}&limit={SearchResultLimit}",
            UriKind.Relative);

        using var response = await SendAsync(requestUri, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<OpenLibrarySearchResponse>(
            responseStream,
            SerializerOptions,
            cancellationToken);

        var results = (payload?.Documents ?? [])
            .Select(MapResult)
            .Where(result => result is not null)
            .Cast<BookMetadataSearchResult>()
            .DistinctBy(result => result.SourceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        CacheSearchResults(normalizedQuery, results);
        return results;
    }

    public async Task<BookCoverImage?> DownloadCoverAsync(
        Uri coverUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(coverUri);
        EnsureOpenLibraryCoverUri(coverUri);

        if (_coverCache.TryGetValue(coverUri, out var cached))
        {
            return cached;
        }

        using var response = await SendAsync(coverUri, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumCoverBytes)
        {
            throw new InvalidDataException("The selected online cover is too large.");
        }

        var extension = GetImageExtension(response.Content.Headers.ContentType);
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream();
        var buffer = new byte[81920];

        while (true)
        {
            var bytesRead = await source.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            if (destination.Length + bytesRead > MaximumCoverBytes)
            {
                throw new InvalidDataException("The selected online cover is too large.");
            }

            destination.Write(buffer, 0, bytesRead);
        }

        if (destination.Length == 0)
        {
            return null;
        }

        var cover = new BookCoverImage(destination.ToArray(), extension);
        CacheCover(coverUri, cover);
        return cover;
    }

    private void CacheSearchResults(
        string query,
        IReadOnlyList<BookMetadataSearchResult> results)
    {
        if (!_searchCache.ContainsKey(query) && _searchCache.Count >= 50)
        {
            var oldestKey = _searchCache.MinBy(entry => entry.Value.CreatedAtUtc).Key;
            _searchCache.Remove(oldestKey);
        }

        _searchCache[query] = new SearchCacheEntry(DateTimeOffset.UtcNow, results);
    }

    private void CacheCover(Uri coverUri, BookCoverImage cover)
    {
        if (!_coverCache.ContainsKey(coverUri) && _coverCache.Count >= 12)
        {
            _coverCache.Remove(_coverCache.Keys.First());
        }

        _coverCache[coverUri] = cover;
    }

    private async Task<HttpResponseMessage> SendAsync(
        Uri requestUri,
        CancellationToken cancellationToken)
    {
        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            var remainingDelay = MinimumRequestInterval
                - (DateTimeOffset.UtcNow - _lastRequestStartedAtUtc);
            if (remainingDelay > TimeSpan.Zero)
            {
                await Task.Delay(remainingDelay, cancellationToken);
            }

            _lastRequestStartedAtUtc = DateTimeOffset.UtcNow;
            return await _httpClient.GetAsync(
                requestUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private static BookMetadataSearchResult? MapResult(OpenLibrarySearchDocument document)
    {
        var edition = document.Editions?.Documents?.FirstOrDefault();
        var title = FirstValue(edition?.Title) ?? FirstValue(document.Title);
        var sourceId = NormalizeOptional(document.Key);
        if (title is null || sourceId is null)
        {
            return null;
        }

        var subtitle = FirstValue(edition?.Subtitle) ?? FirstValue(document.Subtitle);
        var seriesCandidate = FirstValue(edition?.Series) ?? FirstValue(document.Series);
        var (seriesName, seriesPosition) = ParseSeries(seriesCandidate);

        if (TryParseSeries(subtitle, out var subtitleSeriesName, out var subtitleSeriesPosition))
        {
            seriesName ??= subtitleSeriesName;
            seriesPosition ??= subtitleSeriesPosition;
            subtitle = null;
        }

        var isbns = Values(edition?.Isbn)
            .Concat(Values(document.Isbn))
            .Select(NormalizeIsbn)
            .Where(value => value is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var coverId = edition?.CoverId ?? document.CoverId;

        return new BookMetadataSearchResult
        {
            SourceName = "Open Library",
            SourceId = sourceId,
            SourceUri = CreateSourceUri(sourceId),
            Title = title,
            Subtitle = subtitle,
            Authors = NormalizeValues(document.Authors),
            SeriesName = seriesName,
            SeriesPosition = seriesPosition,
            OriginalPublicationYear = document.FirstPublishYear is >= 1000 and <= 9999
                ? document.FirstPublishYear
                : null,
            Publisher = FirstValue(edition?.Publishers) ?? FirstValue(document.Publishers),
            Description = FirstValue(document.FirstSentence),
            Genres = NormalizeValues(document.Subjects).Take(8).ToArray(),
            Language = GetLanguageName(
                FirstValue(edition?.Languages) ?? FirstValue(document.Languages)),
            Isbn10 = isbns.FirstOrDefault(value => value.Length == 10),
            Isbn13 = isbns.FirstOrDefault(value => value.Length == 13),
            CoverUri = coverId is > 0
                ? new Uri($"https://covers.openlibrary.org/b/id/{coverId}-L.jpg?default=false")
                : null,
        };
    }

    private static (string? Name, string? Position) ParseSeries(string? value)
    {
        if (TryParseSeries(value, out var name, out var position))
        {
            return (name, position);
        }

        return (NormalizeOptional(value), null);
    }

    private static bool TryParseSeries(
        string? value,
        out string? seriesName,
        out string? seriesPosition)
    {
        seriesName = null;
        seriesPosition = null;

        var normalized = NormalizeOptional(value);
        if (normalized is null)
        {
            return false;
        }

        var match = SeriesPattern().Match(normalized);
        if (!match.Success)
        {
            return false;
        }

        seriesName = NormalizeOptional(match.Groups["series"].Value);
        seriesPosition = NormalizeSeriesPosition(match.Groups["position"].Value);
        return seriesName is not null && seriesPosition is not null;
    }

    private static string NormalizeSeriesPosition(string position)
    {
        var normalized = position.Trim();
        if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
        {
            return normalized;
        }

        var romanValue = ParseRomanNumeral(normalized);
        return romanValue > 0
            ? romanValue.ToString(CultureInfo.InvariantCulture)
            : normalized;
    }

    private static int ParseRomanNumeral(string value)
    {
        var total = 0;
        var previous = 0;
        foreach (var character in value.ToUpperInvariant().Reverse())
        {
            var current = character switch
            {
                'I' => 1,
                'V' => 5,
                'X' => 10,
                'L' => 50,
                'C' => 100,
                'D' => 500,
                'M' => 1000,
                _ => 0,
            };

            if (current == 0)
            {
                return 0;
            }

            total += current < previous ? -current : current;
            previous = current;
        }

        return total;
    }

    private static string? GetLanguageName(string? code)
    {
        var normalized = NormalizeOptional(code);
        if (normalized is null)
        {
            return null;
        }

        return LanguageNames.TryGetValue(normalized, out var name) ? name : normalized;
    }

    private static Uri CreateSourceUri(string sourceId)
    {
        var path = sourceId.StartsWith('/') ? sourceId : $"/{sourceId}";
        return new Uri(new Uri("https://openlibrary.org"), path);
    }

    private static string NormalizeSearchQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        var normalized = Regex.Replace(query.Trim(), @"\s+", " ");
        return normalized.Length <= 200 ? normalized : normalized[..200];
    }

    private static IReadOnlyList<string> NormalizeValues(JsonElement element) =>
        NormalizeValues(Values(element));

    private static IReadOnlyList<string> NormalizeValues(IEnumerable<string> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string? NormalizeIsbn(string value)
    {
        var normalized = new string(value.Where(character =>
            char.IsDigit(character) || character is 'X' or 'x').ToArray()).ToUpperInvariant();
        return normalized.Length is 10 or 13 ? normalized : null;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? FirstValue(JsonElement? element) =>
        element is { } value ? Values(value).FirstOrDefault() : null;

    private static IEnumerable<string> Values(JsonElement? element)
    {
        if (element is not { } value)
        {
            return [];
        }

        return Values(value);
    }

    private static IEnumerable<string> Values(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => [element.GetString() ?? string.Empty],
            JsonValueKind.Array => element.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString() ?? string.Empty)
                .ToArray(),
            _ => [],
        };
    }

    private static string GetImageExtension(MediaTypeHeaderValue? contentType) =>
        contentType?.MediaType?.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => throw new InvalidDataException("Open Library returned an unsupported cover image format."),
        };

    private static void EnsureOpenLibraryCoverUri(Uri coverUri)
    {
        if (!coverUri.IsAbsoluteUri
            || coverUri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(
                coverUri.Host,
                "covers.openlibrary.org",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The cover address is not an Open Library cover URL.");
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri("https://openlibrary.org/"),
            Timeout = TimeSpan.FromSeconds(15),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "ListenShelf/0.1 (+https://github.com/apelpapa/ListenShelf-Audiobook-Player)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    [GeneratedRegex(
        @"^(?<series>.+?)(?:\s*,?\s+Book\s+|\s*#\s*)(?<position>\d+(?:\.\d+)?|[IVXLCDM]+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SeriesPattern();

    private sealed record SearchCacheEntry(
        DateTimeOffset CreatedAtUtc,
        IReadOnlyList<BookMetadataSearchResult> Results);

    private sealed class OpenLibrarySearchResponse
    {
        [JsonPropertyName("docs")]
        public IReadOnlyList<OpenLibrarySearchDocument>? Documents { get; init; }
    }

    private sealed class OpenLibrarySearchDocument
    {
        [JsonPropertyName("key")]
        public string? Key { get; init; }

        [JsonPropertyName("title")]
        public JsonElement Title { get; init; }

        [JsonPropertyName("subtitle")]
        public JsonElement Subtitle { get; init; }

        [JsonPropertyName("author_name")]
        public JsonElement Authors { get; init; }

        [JsonPropertyName("first_publish_year")]
        public int? FirstPublishYear { get; init; }

        [JsonPropertyName("publisher")]
        public JsonElement Publishers { get; init; }

        [JsonPropertyName("isbn")]
        public JsonElement Isbn { get; init; }

        [JsonPropertyName("language")]
        public JsonElement Languages { get; init; }

        [JsonPropertyName("subject")]
        public JsonElement Subjects { get; init; }

        [JsonPropertyName("series")]
        public JsonElement Series { get; init; }

        [JsonPropertyName("cover_i")]
        public long? CoverId { get; init; }

        [JsonPropertyName("first_sentence")]
        public JsonElement FirstSentence { get; init; }

        [JsonPropertyName("editions")]
        public OpenLibraryEditionResults? Editions { get; init; }
    }

    private sealed class OpenLibraryEditionResults
    {
        [JsonPropertyName("docs")]
        public IReadOnlyList<OpenLibraryEditionDocument>? Documents { get; init; }
    }

    private sealed class OpenLibraryEditionDocument
    {
        [JsonPropertyName("title")]
        public JsonElement Title { get; init; }

        [JsonPropertyName("subtitle")]
        public JsonElement Subtitle { get; init; }

        [JsonPropertyName("publisher")]
        public JsonElement Publishers { get; init; }

        [JsonPropertyName("language")]
        public JsonElement Languages { get; init; }

        [JsonPropertyName("isbn")]
        public JsonElement Isbn { get; init; }

        [JsonPropertyName("series")]
        public JsonElement Series { get; init; }

        [JsonPropertyName("cover_i")]
        public long? CoverId { get; init; }
    }
}
