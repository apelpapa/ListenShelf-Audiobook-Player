using ListenShelf.Application.Library;

namespace ListenShelf.Desktop.Services;

public interface IBookMetadataEditorService
{
    Task<BookMetadataEditResult?> EditAsync(
        LibraryBook book,
        AudiobookMetadataSuggestions suggestions);
}
