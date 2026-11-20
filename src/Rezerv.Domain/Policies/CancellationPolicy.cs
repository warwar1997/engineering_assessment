namespace Rezerv.Domain.Policies;

public static class CancellationPolicy
{
    /// <summary>Cancellations need to be made strictly more than this long before class start to get a refund.</summary>
    public static readonly TimeSpan RefundWindow = TimeSpan.FromHours(4);

    public static bool IsRefundable(DateTime classStart, DateTime cancelledAt) =>
        classStart - cancelledAt > RefundWindow;
}
