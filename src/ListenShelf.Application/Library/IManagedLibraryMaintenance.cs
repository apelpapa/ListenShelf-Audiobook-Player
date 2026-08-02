namespace ListenShelf.Application.Library;

public interface IManagedLibraryMaintenance
{
    string ManagedLibraryPath { get; }

    ManagedLibraryRecoveryResult RecoverAudiobook(string orphanedFilePath);

    ManagedLibraryCleanupResult CleanUp(string orphanedPath);
}
