using Hangfire;
using Microsoft.Extensions.Logging;
using Rezerv.Application.Waitlist;

namespace Rezerv.Infrastructure.Jobs;

/// <summary>
/// Recurring job: once a class has ended, anyone still on its waitlist gets their
/// reserved credit back and the entry is marked Expired.
/// </summary>
public class WaitlistExpiryJob(IWaitlistService waitlist, ILogger<WaitlistExpiryJob> logger)
{
    public const string JobId = "waitlist-expiry";

    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    [AutomaticRetry(Attempts = 0)] // it runs every minute anyway
    public async Task RunAsync(CancellationToken ct)
    {
        var result = await waitlist.ExpireEndedAsync(ct);
        if (result.ExpiredEntries > 0)
            logger.LogInformation("Waitlist expiry job released {Count} reservations", result.ExpiredEntries);
    }
}
