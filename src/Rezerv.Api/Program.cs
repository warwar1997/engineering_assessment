using System.Reflection;
using System.Text.Json.Serialization;
using Hangfire;
using Hangfire.InMemory;
using Microsoft.OpenApi.Models;
using Rezerv.Api.Infrastructure;
using Rezerv.Application;
using Rezerv.Infrastructure;
using Rezerv.Infrastructure.Jobs;
using Rezerv.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Rezerv Booking API",
        Version = "v1",
        Description = "Simplified studio booking engine: packages, timetable, bookings, cancellations and waitlists."
    });
    foreach (var xml in new[] { "Rezerv.Api.xml", "Rezerv.Application.xml" })
    {
        var path = Path.Combine(AppContext.BaseDirectory, xml);
        if (File.Exists(path))
            c.IncludeXmlComments(path);
    }
    c.SupportNonNullableReferenceTypes();
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var jobsEnabled = builder.Configuration.GetValue("Jobs:Enabled", true);
if (jobsEnabled)
{
    // In-memory storage keeps the setup to MySQL + Redis. See README for the production option.
    builder.Services.AddHangfire(c => c
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UseInMemoryStorage(new InMemoryStorageOptions { MaxExpirationTime = TimeSpan.FromHours(1) }));
    builder.Services.AddHangfireServer(o => o.WorkerCount = 2);
}

var app = builder.Build();

app.UseExceptionHandler();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Rezerv Booking API v1");
    c.DisplayRequestDuration();
});

if (jobsEnabled)
{
    app.UseHangfireDashboard("/hangfire", new DashboardOptions { Authorization = [new OpenHangfireDashboardFilter()] });
    app.Services.GetRequiredService<IRecurringJobManager>().AddOrUpdate<WaitlistExpiryJob>(
        WaitlistExpiryJob.JobId, job => job.RunAsync(CancellationToken.None), Cron.Minutely());
}

app.MapControllers();
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).ExcludeFromDescription();

await app.Services.InitialiseDatabaseAsync(seed: app.Configuration.GetValue("Database:SeedOnStartup", true));

app.Run();
