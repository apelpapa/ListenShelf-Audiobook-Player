using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ListenShelf.Desktop.ViewModels;

public sealed partial class AboutViewModel
{
    public bool CanCopyTroubleshootingDetails => !IsCopyingDetails;
    public bool HasCopyStatus => !string.IsNullOrEmpty(CopyStatus);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCopyTroubleshootingDetails))]
    [NotifyCanExecuteChangedFor(nameof(CopyTroubleshootingDetailsCommand))]
    private bool _isCopyingDetails;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCopyStatus))]
    private string _copyStatus = string.Empty;

    [ObservableProperty]
    private bool _isTroubleshootingPreviewExpanded;

    [RelayCommand(CanExecute = nameof(CanCopyTroubleshootingDetails))]
    private async Task CopyTroubleshootingDetailsAsync()
    {
        if (!CanCopyTroubleshootingDetails) return;
        IsCopyingDetails = true;
        CopyStatus = string.Empty;
        try
        {
            if (clipboardService is not null && await clipboardService.SetTextAsync(TroubleshootingDetails))
                CopyStatus = "Troubleshooting details copied. Paste them into your issue report when ready.";
            else
                ShowCopyFailure();
        }
        catch (Exception)
        {
            ShowCopyFailure();
        }
        finally
        {
            IsCopyingDetails = false;
        }
    }

    private void ShowCopyFailure()
    {
        IsTroubleshootingPreviewExpanded = true;
        CopyStatus = "Could not copy to the clipboard. Select and copy the details below, or try again.";
    }
}
