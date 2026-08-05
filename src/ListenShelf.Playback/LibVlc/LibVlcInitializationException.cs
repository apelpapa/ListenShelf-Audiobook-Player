namespace ListenShelf.Playback.LibVlc;

public sealed class LibVlcInitializationException : Exception
{
    public LibVlcInitializationException(
        string message,
        string platformHelp,
        string runtimeDescription,
        Exception innerException)
        : base(message, innerException)
    {
        PlatformHelp = platformHelp;
        RuntimeDescription = runtimeDescription;
    }

    public string PlatformHelp { get; }

    public string RuntimeDescription { get; }
}
