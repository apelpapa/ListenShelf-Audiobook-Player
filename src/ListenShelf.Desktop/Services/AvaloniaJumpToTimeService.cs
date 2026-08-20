using Avalonia.Controls;
using ListenShelf.Desktop.ViewModels;
using ListenShelf.Desktop.Views;

namespace ListenShelf.Desktop.Services;

public sealed class AvaloniaJumpToTimeService(Window owner) : IJumpToTimeService
{
    public Task<TimeSpan?> ShowAsync(TimeSpan position, TimeSpan duration)
    {
        var dialog = new JumpToTimeWindow
        {
            DataContext = new JumpToTimeViewModel(position, duration),
        };
        return dialog.ShowDialog<TimeSpan?>(owner);
    }
}
