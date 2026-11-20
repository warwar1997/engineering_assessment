namespace Rezerv.Domain.Enums;

public enum BookingStatus
{
    Confirmed = 1,
    Cancelled = 2
}

public enum BookingSource
{
    Direct = 1,
    WaitlistPromotion = 2
}

public enum WaitlistStatus
{
    /// <summary>In the queue, holding one reserved credit.</summary>
    Waiting = 1,
    /// <summary>Moved into a confirmed booking.</summary>
    Promoted = 2,
    /// <summary>The user left the waitlist themselves.</summary>
    Cancelled = 3,
    /// <summary>The class ended while the user was still waiting.</summary>
    Expired = 4,
    /// <summary>Was first in line but could not be promoted (e.g. package expired, overlapping booking).</summary>
    Skipped = 5
}

public enum CreditTransactionType
{
    Purchase = 1,
    BookingDebit = 2,
    CancellationRefund = 3,
    WaitlistReserve = 4,
    WaitlistRelease = 5,
    WaitlistPromotionDebit = 6
}
