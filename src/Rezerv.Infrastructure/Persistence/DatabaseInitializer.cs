using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rezerv.Infrastructure.Persistence.Seed;

namespace Rezerv.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    /// <summary>Applies migrations (retrying while MySQL starts up) and seeds sample data.</summary>
    public static async Task InitialiseDatabaseAsync(this IServiceProvider services, bool seed, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInitializer");

        const int maxAttempts = 10;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync(ct);
                break;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                logger.LogWarning("Database not ready (attempt {Attempt}/{Max}): {Message}", attempt, maxAttempts, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }

        if (seed)
            await scope.ServiceProvider.GetRequiredService<DbSeeder>().SeedAsync(ct);
    }
}
