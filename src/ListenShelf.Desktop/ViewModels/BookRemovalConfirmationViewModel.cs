using ListenShelf.Application.Library;

namespace ListenShelf.Desktop.ViewModels;

public sealed class BookRemovalConfirmationViewModel
{
    public BookRemovalConfirmationViewModel(LibraryBook book)
    {
        ArgumentNullException.ThrowIfNull(book);

        Heading = $"Remove “{book.Title}”?";
        FileName = Path.GetFileName(book.FilePath);
    }

    public string Heading { get; }

    public string FileName { get; }
}
