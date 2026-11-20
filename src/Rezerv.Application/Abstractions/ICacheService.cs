namespace Rezerv.Application.Abstractions;

public interface ICacheService
{
    /// <summary>Cache-aside read. Falls back to <paramref name="factory"/> if the cache is unavailable.</summary>
    Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl, CancellationToken cancellationToken = default);

    /// <summary>Current value of a namespace version counter (used to build cache keys).</summary>
    Task<long> GetVersionAsync(string @namespace, CancellationToken cancellationToken = default);

    /// <summary>Bumps a namespace version, which effectively invalidates every key built from the old version.</summary>
    Task BumpVersionAsync(string @namespace, CancellationToken cancellationToken = default);
}

public static class CacheKeys
{
    public const string TimetableNamespace = "timetable";

    public static string PackageCatalog(int? businessId) => $"packages:catalog:{businessId?.ToString() ?? "all"}";

    public static string Timetable(long version, int? businessId, DateOnly? date) =>
        $"timetable:v{version}:b:{businessId?.ToString() ?? "all"}:d:{date?.ToString("yyyy-MM-dd") ?? "all"}";
}
