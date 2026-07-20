using Avalonia.Controls;
using ListenShelf.Application.Library;
using ListenShelf.Desktop.ViewModels;
using ListenShelf.Desktop.Views;

namespace ListenShelf.Desktop.Services;

public sealed class AvaloniaBookMetadataEditorService(Window owner) : IBookMetadataEditorService
{
    public Task<AudiobookMetadata?> EditAsync(
        LibraryBook book,
        AudiobookMetadataSuggestions suggestions)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(suggestions);

        var dialog = new BookMetadataEditorWindow
        {
            DataContext = new BookMetadataEditorViewModel(book.Metadata, suggestions),
        };

        return dialog.ShowDialog<AudiobookMetadata?>(owner);
    }
}
