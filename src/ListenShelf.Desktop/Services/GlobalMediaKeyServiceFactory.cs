namespace ListenShelf.Desktop.Services;

public static class GlobalMediaKeyServiceFactory
{
    public static IGlobalMediaKeyService Create() =>
        new WindowsMediaKeyService();
}
