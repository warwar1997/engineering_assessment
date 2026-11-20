namespace Rezerv.Infrastructure.Redis;

public class RedisOptions
{
    public const string Section = "Redis";

    public string ConnectionString { get; set; } = "localhost:6379";
    public string KeyPrefix { get; set; } = "rezerv:";

    /// <summary>How long a lock lives if the holder dies without releasing it.</summary>
    public int LockTtlMs { get; set; } = 10_000;

    /// <summary>How long a request waits for a contended lock before giving up with 409.</summary>
    public int LockWaitMs { get; set; } = 5_000;

    /// <summary>Base delay between acquisition attempts (jitter is added).</summary>
    public int LockRetryMs { get; set; } = 25;
}
