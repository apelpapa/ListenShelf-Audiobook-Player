namespace ListenShelf.Application.Library;

public static class AudiobookFileFormats
{
    public static IReadOnlyList<string> SupportedExtensions { get; } =
        [".m4b", ".m4a", ".mp3"];

    public static bool IsSupported(string filePath) =>
        !string.IsNullOrWhiteSpace(filePath)
        && SupportedExtensions.Contains(
            Path.GetExtension(filePath),
            StringComparer.OrdinalIgnoreCase);
}
