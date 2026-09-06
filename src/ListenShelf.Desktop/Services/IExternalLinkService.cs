namespace ListenShelf.Desktop.Services;

public interface IExternalLinkService
{
    Task<bool> OpenAsync(Uri uri);
}
