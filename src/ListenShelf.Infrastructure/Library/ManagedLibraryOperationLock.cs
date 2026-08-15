using System.Security.Cryptography;
using System.Text;

namespace ListenShelf.Infrastructure.Library;

// Keep imports, repairs, removals, cleanup, and backup replacement from racing
// across windows/processes. Acquire and dispose on the same synchronous thread.
internal sealed class ManagedLibraryOperationLock(Mutex mutex) : IDisposable
{
    public static IDisposable Acquire(string databasePath, CancellationToken token = default, Action? waiting = null)
    {
        var key = OperatingSystem.IsWindows() ? databasePath.ToUpperInvariant() : databasePath;
        // Preserve the existing import mutex name for compatibility.
        var mutex = new Mutex(false, "ListenShelf.Import." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))));
        try
        {
            var reported = false;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (mutex.WaitOne(TimeSpan.FromMilliseconds(100))) return new ManagedLibraryOperationLock(mutex);
                }
                catch (AbandonedMutexException)
                {
                    // Interrupted files stay visible for explicit Storage Care recovery.
                    return new ManagedLibraryOperationLock(mutex);
                }

                if (!reported)
                {
                    waiting?.Invoke();
                    reported = true;
                }
            }
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
