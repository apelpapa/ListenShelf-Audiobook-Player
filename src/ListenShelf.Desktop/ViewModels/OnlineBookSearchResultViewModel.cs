using ListenShelf.Application.Library;

namespace ListenShelf.Desktop.ViewModels;

public sealed class OnlineBookSearchResultViewModel
{
    public OnlineBookSearchResultViewModel(BookMetadataSearchResult result)
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));

        TitleText = string.IsNullOrWhiteSpace(result.Subtitle)
            ? result.Title
            : $"{result.Title}: {result.Subtitle}";
        AuthorText = result.Authors.Count > 0
            ? string.Join(", ", result.Authors)
            : "Author unknown";

        var details = new List<string>();
        if (result.OriginalPublicationYear is { } year)
        {
            details.Add(year.ToString());
        }

        if (!string.IsNullOrWhiteSpace(result.SeriesName))
        {
            details.Add(string.IsNullOrWhiteSpace(result.SeriesPosition)
                ? result.SeriesName
                : $"{result.SeriesName} · Book {result.SeriesPosition}");
        }

        if (!string.IsNullOrWhiteSpace(result.Publisher))
        {
            details.Add(result.Publisher);
        }

        DetailsText = details.Count > 0 ? string.Join(" · ", details) : "No additional details";
    }

    public BookMetadataSearchResult Result { get; }

    public string TitleText { get; }

    public string AuthorText { get; }

    public string DetailsText { get; }

    public bool HasCover => Result.CoverUri is not null;
}
