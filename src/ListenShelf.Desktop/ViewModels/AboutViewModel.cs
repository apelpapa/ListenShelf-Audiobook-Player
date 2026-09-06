using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ListenShelf.Desktop.Services;

namespace ListenShelf.Desktop.ViewModels;

public sealed partial class AboutViewModel(IExternalLinkService? linkService) : ViewModelBase
{
    public ApplicationAboutInfo Info { get; } = ApplicationAboutInfo.Current;
    public string VersionText => $"Version {Info.Version}";
    public string LicenseText => "Source-code license: GNU GPL v3 only (GPL-3.0-only).";
    public string RepositoryUrl => ProjectLinks.Repository.AbsoluteUri;
    public string IssuesUrl => ProjectLinks.Issues.AbsoluteUri;
    public string LicenseUrl => ProjectLinks.License.AbsoluteUri;
    public bool CanOpenLinks => !IsOpeningLink;
    public bool HasLinkError => !string.IsNullOrEmpty(LinkError);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpenLinks))]
    [NotifyCanExecuteChangedFor(nameof(OpenRepositoryCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenIssuesCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenLicenseCommand))]
    private bool _isOpeningLink;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLinkError))]
    private string _linkError = string.Empty;

    [ObservableProperty]
    private string _failedLinkUrl = string.Empty;

    [RelayCommand(CanExecute = nameof(CanOpenLinks))]
    private Task OpenRepositoryAsync() => OpenLinkAsync(ProjectLinks.Repository);

    [RelayCommand(CanExecute = nameof(CanOpenLinks))]
    private Task OpenIssuesAsync() => OpenLinkAsync(ProjectLinks.Issues);

    [RelayCommand(CanExecute = nameof(CanOpenLinks))]
    private Task OpenLicenseAsync() => OpenLinkAsync(ProjectLinks.License);

    private async Task OpenLinkAsync(Uri uri)
    {
        if (!CanOpenLinks) return;
        IsOpeningLink = true;
        LinkError = string.Empty;
        FailedLinkUrl = string.Empty;
        try
        {
            if (linkService is null || !await linkService.OpenAsync(uri)) ShowLinkError(uri);
        }
        catch (Exception)
        {
            ShowLinkError(uri);
        }
        finally
        {
            IsOpeningLink = false;
        }
    }

    private void ShowLinkError(Uri uri)
    {
        FailedLinkUrl = uri.AbsoluteUri;
        LinkError = "Your default browser could not be opened. Copy this address into your browser:";
    }
}
