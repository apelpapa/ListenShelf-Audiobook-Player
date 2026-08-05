namespace ListenShelf.Infrastructure.Storage;

public sealed class ListenShelfDatabaseException : Exception
{
    public ListenShelfDatabaseException(
        ListenShelfDatabaseFailureKind kind,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public ListenShelfDatabaseFailureKind Kind { get; }
}
