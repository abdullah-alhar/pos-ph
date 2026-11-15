namespace VetPos.App.Infrastructure;

/// <summary>
/// Only one copy of the till may run (two copies could sell the same stock twice). Starting it
/// again brings the open window to the front instead.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\VetPOS.SingleInstance";
    private const string SignalName = @"Local\VetPOS.Activate";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _signal;
    private readonly RegisteredWaitHandle? _wait;

    private SingleInstance(Mutex mutex, EventWaitHandle signal, Action onActivate)
    {
        _mutex = mutex;
        _signal = signal;
        _wait = ThreadPool.RegisterWaitForSingleObject(signal, (_, _) => onActivate(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>
    /// Returns the guard for the first copy, or null after asking the first copy to come to the
    /// front (this copy should then exit).
    /// </summary>
    public static SingleInstance? TryAcquire(Action onActivate)
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            if (EventWaitHandle.TryOpenExisting(SignalName, out var existing))
            {
                using (existing) existing.Set();
            }
            return null;
        }
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
        return new SingleInstance(mutex, signal, onActivate);
    }

    public void Dispose()
    {
        _wait?.Unregister(null);
        _signal.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
