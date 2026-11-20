namespace Rezerv.Application.Abstractions;

public interface IDistributedLockService
{
    /// <summary>
    /// Acquires all the given locks in the order supplied (callers must use a consistent
    /// order to avoid lock cycles). Throws a RESOURCE_BUSY domain exception if a lock
    /// can't be taken within the configured wait time.
    /// Dispose the returned handle to release every lock.
    /// </summary>
    Task<IAsyncDisposable> AcquireAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default);
}

public static class LockKeys
{
    // Ordering rule used everywhere: schedule lock first, then user lock(s) in ascending id order.
    public static string Schedule(int scheduleId) => $"lock:schedule:{scheduleId}";
    public static string User(int userId) => $"lock:user:{userId}";
}
