namespace ListenShelf.Application.Library;

public interface IManagedFileVerifier
{
    // Null checks the entire current managed catalog; an ID checks only that book.
    ManagedFileVerificationReport Verify(Guid? bookId = null,
        IProgress<ManagedFileVerificationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
