using System.Collections.Concurrent;
using Rezerv.Application.Abstractions;

namespace Rezerv.UnitTests.Support;

/// <summary>In-process stand-in for the Redis lock with the same semantics (per-key mutual exclusion).</summary>
public sealed class InMemoryLockService : IDistributedLockService
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task<IAsyncDisposable> AcquireAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
    {
        var taken = new List<SemaphoreSlim>();
        foreach (var key in keys)
        {
            var sem = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await sem.WaitAsync(cancellationToken);
            taken.Add(sem);
        }
        return new Releaser(taken);
    }

    private sealed class Releaser(List<SemaphoreSlim> taken) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            for (var i = taken.Count - 1; i >= 0; i--)
                taken[i].Release();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>Lock that does nothing - used to prove the database concurrency tokens alone prevent overbooking.</summary>
public sealed class NoLockService : IDistributedLockService
{
    public Task<IAsyncDisposable> AcquireAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default) =>
        Task.FromResult<IAsyncDisposable>(new Noop());

    private sealed class Noop : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

public sealed class PassThroughCache : ICacheService
{
    public Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl, CancellationToken cancellationToken = default) => factory();
    public Task<long> GetVersionAsync(string @namespace, CancellationToken cancellationToken = default) => Task.FromResult(0L);
    public Task BumpVersionAsync(string @namespace, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
