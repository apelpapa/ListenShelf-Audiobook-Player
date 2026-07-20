using ListenShelf.Application.Library;

namespace ListenShelf.Desktop.Services;

public interface IBookMetadataEditorService
{
    Task<AudiobookMetadata?> EditAsync(LibraryBook book);
}
