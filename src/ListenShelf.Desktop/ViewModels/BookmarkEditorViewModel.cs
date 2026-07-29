using CommunityToolkit.Mvvm.ComponentModel;
using ListenShelf.Application.Bookmarks;
using ListenShelf.Desktop.Services;

namespace ListenShelf.Desktop.ViewModels;

public sealed partial class BookmarkEditorViewModel : ViewModelBase
{
    public BookmarkEditorViewModel(PlaybackBookmark? bookmark)
    {
        IsEditing = bookmark is not null;
        _name = bookmark?.Name ?? string.Empty;
        _note = bookmark?.Note ?? string.Empty;
    }

    public bool IsEditing { get; }

    public string WindowTitle => IsEditing ? "Edit bookmark" : "Add bookmark";

    public string Heading => WindowTitle;

    public string Description => IsEditing
        ? "Change the optional name or note. The saved position will stay the same."
        : "The current listening position will be saved. A name and note are optional.";

    public string SaveButtonText => IsEditing ? "Save changes" : "Add bookmark";

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _note;

    public BookmarkEditResult CreateResult() =>
        new(
            NormalizeOptionalText(Name),
            NormalizeOptionalText(Note));

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
}
