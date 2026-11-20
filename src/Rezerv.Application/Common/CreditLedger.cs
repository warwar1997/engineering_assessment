using Rezerv.Application.Abstractions;
using Rezerv.Domain.Entities;
using Rezerv.Domain.Enums;

namespace Rezerv.Application.Common;

internal static class CreditLedger
{
    public static void Record(IAppDbContext db, UserPackage package, CreditTransactionType type, DateTime now,
        Booking? booking = null, WaitlistEntry? waitlistEntry = null)
    {
        var (remaining, reserved) = type switch
        {
            CreditTransactionType.Purchase => (package.TotalCredits, 0),
            CreditTransactionType.BookingDebit => (-1, 0),
            CreditTransactionType.CancellationRefund => (1, 0),
            CreditTransactionType.WaitlistReserve => (0, 1),
            CreditTransactionType.WaitlistRelease => (0, -1),
            CreditTransactionType.WaitlistPromotionDebit => (-1, -1),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };

        db.CreditTransactions.Add(new CreditTransaction
        {
            UserPackage = package,
            Type = type,
            RemainingDelta = remaining,
            ReservedDelta = reserved,
            Booking = booking,
            WaitlistEntry = waitlistEntry,
            CreatedAt = now
        });
    }
}
