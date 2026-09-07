using Avalonia.Input.Platform;

namespace ListenShelf.Desktop.Services;

public sealed class AvaloniaClipboardTextService(Func<IClipboard?> getClipboard) : IClipboardTextService
{
    public async Task<bool> SetTextAsync(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var clipboard = getClipboard();
        if (clipboard is null) return false;
        await clipboard.SetTextAsync(text);
        // Keep copied details available if the user closes ListenShelf before pasting.
        await clipboard.FlushAsync();
        return true;
    }
}
