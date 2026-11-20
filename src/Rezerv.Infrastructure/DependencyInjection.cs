using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Rezerv.Application.Abstractions;
using Rezerv.Infrastructure.Jobs;
using Rezerv.Infrastructure.Persistence;
using Rezerv.Infrastructure.Persistence.Seed;
using Rezerv.Infrastructure.Redis;
using StackExchange.Redis;

namespace Rezerv.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("MySql")
                               ?? throw new InvalidOperationException("ConnectionStrings:MySql is not configured.");

        // Fixed server version instead of AutoDetect so the app doesn't need a DB connection just to build the container.
        var serverVersion = new MySqlServerVersion(Version.Parse(configuration["Database:ServerVersion"] ?? "8.0.36"));

        services.AddDbContext<AppDbContext>(options =>
            options.UseMySql(connectionString, serverVersion, mysql =>
                mysql.EnableRetryOnFailure(maxRetryCount: 3)));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<DbSeeder>();

        services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.Section));
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<RedisOptions>>().Value;
            var config = ConfigurationOptions.Parse(opts.ConnectionString);
            config.AbortOnConnectFail = false; // keep retrying in the background instead of crashing on startup
            return ConnectionMultiplexer.Connect(config);
        });
        services.AddSingleton<IDistributedLockService, RedisDistributedLockService>();
        services.AddSingleton<ICacheService, RedisCacheService>();

        services.AddScoped<WaitlistExpiryJob>();

        return services;
    }
}
