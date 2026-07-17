namespace ListenShelf.Application.Library;

public interface ILibrarySettingsStore
{
    LibraryStorageMode? GetDefaultStorageMode();

    void SaveDefaultStorageMode(LibraryStorageMode storageMode);
}
