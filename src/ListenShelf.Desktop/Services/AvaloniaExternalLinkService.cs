using Avalonia.Controls;

namespace ListenShelf.Desktop.Services;

public sealed class AvaloniaExternalLinkService(Window owner) : IExternalLinkService
{
    public Task<bool> OpenAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("External project links must use HTTPS.", nameof(uri));
        return owner.Launcher.LaunchUriAsync(uri);
    }
}
