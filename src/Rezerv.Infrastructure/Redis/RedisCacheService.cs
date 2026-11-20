using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rezerv.Application.Abstractions;
using StackExchange.Redis;

namespace Rezerv.Infrastructure.Redis;

/// <summary>
/// Cache-aside on top of Redis. The cache is an optimisation only: if Redis is
/// unavailable every method degrades to "go to the database" instead of failing the request.
/// </summary>
public sealed class RedisCacheService(
    IConnectionMultiplexer redis,
    IOptions<RedisOptions> options,
    ILogger<RedisCacheService> logger) : ICacheService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _prefix = options.Value.KeyPrefix;

    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var redisKey = (RedisKey)(_prefix + key);
        try
        {
            var cached = await redis.GetDatabase().StringGetAsync(redisKey);
            if (cached.HasValue)
                return JsonSerializer.Deserialize<T>(cached.ToString(), Json)!;
        }
        catch (Exception ex) when (ex is RedisException or JsonException or TimeoutException)
        {
            logger.LogWarning(ex, "Cache read failed for {Key}, falling back to database", key);
        }

        var value = await factory();

        try
        {
            await redis.GetDatabase().StringSetAsync(redisKey, JsonSerializer.Serialize(value, Json), ttl);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(ex, "Cache write failed for {Key}", key);
        }

        return value;
    }

    public async Task<long> GetVersionAsync(string @namespace, CancellationToken cancellationToken = default)
    {
        try
        {
            var value = await redis.GetDatabase().StringGetAsync(VersionKey(@namespace));
            return value.HasValue ? (long)value : 0;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(ex, "Could not read cache version for {Namespace}", @namespace);
            return -1; // a key nobody writes to under normal operation, so effectively a cache miss
        }
    }

    public async Task BumpVersionAsync(string @namespace, CancellationToken cancellationToken = default)
    {
        try
        {
            await redis.GetDatabase().StringIncrementAsync(VersionKey(@namespace));
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            // Worst case the timetable shows slightly stale counts until the TTL passes.
            // Booking correctness never depends on the cache.
            logger.LogWarning(ex, "Could not bump cache version for {Namespace}", @namespace);
        }
    }

    private RedisKey VersionKey(string @namespace) => _prefix + "version:" + @namespace;
}
