namespace ListenShelf.Desktop.Services;

public static class GlobalMediaKeyServiceFactory
{
    public static IGlobalMediaKeyService Create() =>
        OperatingSystem.IsWindows()
            ? new WindowsMediaKeyService()
            : new NoOpGlobalMediaKeyService();
}
