using Hangfire.Dashboard;

namespace Rezerv.Api.Infrastructure;

/// <summary>Demo only: lets anyone open the Hangfire dashboard. Would sit behind admin auth in a real deployment.</summary>
public sealed class OpenHangfireDashboardFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => true;
}
