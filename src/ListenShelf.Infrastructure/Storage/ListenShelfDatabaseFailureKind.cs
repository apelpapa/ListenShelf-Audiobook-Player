namespace ListenShelf.Infrastructure.Storage;

public enum ListenShelfDatabaseFailureKind
{
    Damaged,
    Unavailable,
    MigrationFailed,
    NewerVersion,
}
