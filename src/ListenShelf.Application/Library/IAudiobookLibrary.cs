namespace ListenShelf.Application.Library;

public interface IAudiobookLibrary
{
    string ManagedLibraryPath { get; }

    IReadOnlyList<LibraryBook> GetBooks();

    LibraryImportResult Import(string sourceFilePath);

    LibraryBook SetCover(Guid bookId, string sourceImagePath);

    LibraryBook SetCover(Guid bookId, ReadOnlyMemory<byte> imageData, string fileExtension);

    LibraryBook UpdateMetadata(Guid bookId, AudiobookMetadata metadata);
}
