using Avalonia.Controls;
using ListenShelf.Desktop.ViewModels;
using ListenShelf.Desktop.Views;

namespace ListenShelf.Desktop.Services;

public sealed class AvaloniaSleepTimerDurationService(Window owner) : ISleepTimerDurationService
{
    public Task<int?> ChooseMinutesAsync()
    {
        var dialog = new CustomSleepTimerWindow
        {
            DataContext = new CustomSleepTimerViewModel(),
        };
        return dialog.ShowDialog<int?>(owner);
    }
}
