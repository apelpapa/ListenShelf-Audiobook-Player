namespace ListenShelf.Desktop.Services;

public interface IClipboardTextService
{
    Task<bool> SetTextAsync(string text);
}
