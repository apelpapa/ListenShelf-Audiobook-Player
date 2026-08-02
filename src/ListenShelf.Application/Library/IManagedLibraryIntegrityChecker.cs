namespace ListenShelf.Application.Library;

public interface IManagedLibraryIntegrityChecker
{
    string ManagedLibraryPath { get; }

    ManagedLibraryIntegrityReport Check();
}
