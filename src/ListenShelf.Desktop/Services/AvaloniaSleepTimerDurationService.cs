using Avalonia.Controls;
using ListenShelf.Desktop.ViewModels;
using ListenShelf.Desktop.Views;

namespace ListenShelf.Desktop.Services;

public sealed class AvaloniaSleepTimerDurationService(Window owner) : ISleepTimerDurationService
{
    public Task<int?> ChooseMinutesAsync(int? initialMinutes)
    {
        var dialog = new CustomSleepTimerWindow
        {
            DataContext = new CustomSleepTimerViewModel(initialMinutes),
        };
        return dialog.ShowDialog<int?>(owner);
    }
}
