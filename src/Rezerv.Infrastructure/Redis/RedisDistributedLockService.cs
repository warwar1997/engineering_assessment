using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rezerv.Application.Abstractions;
using Rezerv.Domain.Common;
using StackExchange.Redis;

namespace Rezerv.Infrastructure.Redis;

/// <summary>
/// Simple single-instance Redis lock: SET key token NX PX ttl to acquire,
/// compare-and-delete Lua script to release (so we never delete someone else's lock
/// after ours has expired).
/// </summary>
public sealed class RedisDistributedLockService(
    IConnectionMultiplexer redis,
    IOptions<RedisOptions> options,
    ILogger<RedisDistributedLockService> logger) : IDistributedLockService
{
    private const string ReleaseScript = """
        if redis.call('get', KEYS[1]) == ARGV[1] then
            return redis.call('del', KEYS[1])
        else
            return 0
        end
        """;

    private readonly RedisOptions _options = options.Value;

    public async Task<IAsyncDisposable> AcquireAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
    {
        var db = redis.GetDatabase();
        var held = new List<(RedisKey Key, string Token)>(keys.Count);

        try
        {
            foreach (var key in keys)
            {
                var redisKey = (RedisKey)(_options.KeyPrefix + key);
                var token = Guid.NewGuid().ToString("N");
                await AcquireOneAsync(db, redisKey, token, cancellationToken);
                held.Add((redisKey, token));
            }
        }
        catch
        {
            await ReleaseAsync(db, held);
            throw;
        }

        return new Handle(this, db, held);
    }

    private async Task AcquireOneAsync(IDatabase db, RedisKey key, string token, CancellationToken ct)
    {
        var ttl = TimeSpan.FromMilliseconds(_options.LockTtlMs);
        var sw = Stopwatch.StartNew();

        while (true)
        {
            if (await db.StringSetAsync(key, token, ttl, When.NotExists))
                return;

            if (sw.ElapsedMilliseconds >= _options.LockWaitMs)
            {
                logger.LogWarning("Timed out after {Elapsed} ms waiting for lock {Key}", sw.ElapsedMilliseconds, key);
                throw new DomainException(ErrorCodes.ResourceBusy,
                    "This class is very busy right now. Please try again in a moment.", ErrorKind.Conflict);
            }

            await Task.Delay(_options.LockRetryMs + Random.Shared.Next(0, _options.LockRetryMs), ct);
        }
    }

    private async Task ReleaseAsync(IDatabase db, IEnumerable<(RedisKey Key, string Token)> held)
    {
        foreach (var (key, token) in held.Reverse())
        {
            try
            {
                var released = (int)await db.ScriptEvaluateAsync(ReleaseScript, [key], [token]);
                if (released == 0)
                    logger.LogWarning("Lock {Key} had already expired before release; DB concurrency tokens guard the write", key);
            }
            catch (Exception ex)
            {
                // Not fatal: the key expires by itself after the TTL.
                logger.LogError(ex, "Failed to release lock {Key}", key);
            }
        }
    }

    private sealed class Handle(RedisDistributedLockService owner, IDatabase db, List<(RedisKey, string)> held) : IAsyncDisposable
    {
        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                await owner.ReleaseAsync(db, held);
        }
    }
}
