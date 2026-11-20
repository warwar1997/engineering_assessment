namespace Rezerv.Application.Common;

internal static class TimeProviderExtensions
{
    /// <summary>All persisted times are UTC <see cref="DateTime"/>s.</summary>
    public static DateTime UtcNow(this TimeProvider clock) => clock.GetUtcNow().UtcDateTime;
}
