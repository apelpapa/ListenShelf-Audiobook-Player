using Avalonia.Controls;
using ListenShelf.Application.Library;
using ListenShelf.Desktop.ViewModels;
using ListenShelf.Desktop.Views;

namespace ListenShelf.Desktop.Services;

public sealed class AvaloniaBookMetadataEditorService(
    Window owner,
    IBookMetadataProvider metadataProvider) : IBookMetadataEditorService
{
    public async Task<BookMetadataEditResult?> EditAsync(
        LibraryBook book,
        AudiobookMetadataSuggestions suggestions)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(suggestions);

        var viewModel = new BookMetadataEditorViewModel(
            book.Metadata,
            suggestions,
            metadataProvider,
            hasExistingCover: !string.IsNullOrWhiteSpace(book.CoverPath));
        var dialog = new BookMetadataEditorWindow
        {
            DataContext = viewModel,
        };

        try
        {
            return await dialog.ShowDialog<BookMetadataEditResult?>(owner);
        }
        finally
        {
            viewModel.Dispose();
        }
    }
}
