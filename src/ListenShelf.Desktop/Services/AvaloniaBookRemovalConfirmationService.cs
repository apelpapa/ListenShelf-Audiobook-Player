using Avalonia.Controls;
using ListenShelf.Application.Library;
using ListenShelf.Desktop.ViewModels;
using ListenShelf.Desktop.Views;

namespace ListenShelf.Desktop.Services;

public sealed class AvaloniaBookRemovalConfirmationService(Window owner)
    : IBookRemovalConfirmationService
{
    public Task<bool> ConfirmRemovalAsync(LibraryBook book)
    {
        ArgumentNullException.ThrowIfNull(book);

        var dialog = new BookRemovalConfirmationWindow
        {
            DataContext = new BookRemovalConfirmationViewModel(book),
        };

        return dialog.ShowDialog<bool>(owner);
    }
}
