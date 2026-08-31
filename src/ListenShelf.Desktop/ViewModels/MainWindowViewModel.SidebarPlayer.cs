namespace ListenShelf.Desktop.ViewModels;

public partial class MainWindowViewModel
{
    public bool HasSidebarPlayer => !_disposed && IsFileLoaded;

    public bool HasSidebarPlayerError => HasSidebarPlayer && !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool CanTogglePlayback => !_disposed && CanControlPlayback;
}
