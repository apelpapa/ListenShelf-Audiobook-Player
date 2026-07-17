namespace ListenShelf.Application.Library;

public interface IAudiobookLibrary
{
    string ManagedLibraryPath { get; }

    IReadOnlyList<LibraryBook> GetBooks();

    LibraryImportResult Import(string sourceFilePath, LibraryStorageMode storageMode);

    LibraryBook SetCover(Guid bookId, string sourceImagePath);
}
