using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;

namespace ListenShelf.Desktop.ViewModels;

public sealed partial class LibraryGroupViewModel : ViewModelBase
{
    private readonly Action<LibraryGroupViewModel>? _openRequested;

    public LibraryGroupViewModel(
        string name,
        IReadOnlyList<LibraryBookItemViewModel> books,
        bool showHeader,
        Action<LibraryGroupViewModel>? openRequested = null)
    {
        Name = name;
        Books = books;
        ShowHeader = showHeader;
        _openRequested = openRequested;
    }

    public string Name { get; }

    public IReadOnlyList<LibraryBookItemViewModel> Books { get; }

    public LibraryBookItemViewModel? PreviewBook => Books.FirstOrDefault();

    public bool ShowHeader { get; }

    public string CountText => Books.Count == 1 ? "1 book" : $"{Books.Count} books";

    public string PreviewTitle => PreviewBook?.Title ?? string.Empty;

    public Bitmap? PreviewCoverImage => PreviewBook?.CoverImage;

    public bool HasPreviewCover => PreviewCoverImage is not null;

    public bool HasNoPreviewCover => !HasPreviewCover;

    public bool HasSecondBook => Books.Count >= 2;

    public bool HasThirdBook => Books.Count >= 3;

    public string ExpansionGlyph => "▶";

    public string InteractionHint => $"Open {Name}";

    [RelayCommand]
    private void OpenGroup()
    {
        if (ShowHeader)
        {
            _openRequested?.Invoke(this);
        }
    }
}
