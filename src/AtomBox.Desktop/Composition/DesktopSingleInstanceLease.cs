namespace AtomBox.Desktop.Composition;

internal sealed class DesktopSingleInstanceLease : IDisposable
{
    private const string MutexName = @"Local\AtomBox.Desktop";
    private static int _processLeaseHeld;
    private readonly Mutex _mutex;
    private bool _ownsMutex;

    private DesktopSingleInstanceLease(Mutex mutex)
    {
        _mutex = mutex;
        _ownsMutex = true;
    }

    public static bool TryAcquire(out DesktopSingleInstanceLease? lease)
    {
        lease = null;
        if (Interlocked.CompareExchange(ref _processLeaseHeld, 1, 0) != 0)
        {
            return false;
        }

        var mutex = new Mutex(initiallyOwned: false, MutexName);
        var acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(TimeSpan.Zero);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                mutex.Dispose();
                Interlocked.Exchange(ref _processLeaseHeld, 0);
                return false;
            }

            lease = new DesktopSingleInstanceLease(mutex);
            return true;
        }
        catch
        {
            if (acquired)
            {
                mutex.ReleaseMutex();
            }

            mutex.Dispose();
            Interlocked.Exchange(ref _processLeaseHeld, 0);
            throw;
        }
    }

    public void Dispose()
    {
        if (!_ownsMutex)
        {
            return;
        }

        _ownsMutex = false;
        _mutex.ReleaseMutex();
        _mutex.Dispose();
        Interlocked.Exchange(ref _processLeaseHeld, 0);
    }
}
