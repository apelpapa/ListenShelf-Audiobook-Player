using ListenShelf.Application.Library;

namespace ListenShelf.Desktop.Services;

public interface IBookRemovalConfirmationService
{
    Task<bool> ConfirmRemovalAsync(LibraryBook book);
}
