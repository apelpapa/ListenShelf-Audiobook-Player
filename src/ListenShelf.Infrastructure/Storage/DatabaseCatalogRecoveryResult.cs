namespace ListenShelf.Infrastructure.Storage;

public sealed record DatabaseCatalogRecoveryResult(
    int RecoveredBookCount,
    int SkippedAudiobookCount,
    string PreservedDatabasePath);
