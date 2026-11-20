using Microsoft.EntityFrameworkCore;
using Rezerv.Application.Abstractions;
using Rezerv.Application.Bookings;
using Rezerv.Application.Common;
using Rezerv.Domain.Entities;
using Rezerv.Domain.Enums;

namespace Rezerv.Application.Waitlist;

internal sealed record Promotion(WaitlistEntry Entry, Booking Booking);

/// <summary>
/// Moves the first eligible waitlisted user into a freed slot.
/// Must be called while holding the schedule lock. Changes are tracked but not saved;
/// the caller saves them together with whatever freed the slot (one transaction).
/// </summary>
internal sealed class WaitlistPromoter(IAppDbContext db, IDistributedLockService locks, BookingRules rules)
{
    public async Task<Promotion?> PromoteNextAsync(TimetableSchedule schedule, DateTime now, LockScope lockScope, CancellationToken ct)
    {
        if (schedule.HasStarted(now))
            return null; // nobody gets promoted into a class that is already running

        var skipped = new List<int>();

        while (!schedule.IsFull)
        {
            var entry = await db.WaitlistEntries
                .Include(w => w.UserPackage)
                .WaitingInOrder(schedule.Id)
                .Where(w => !skipped.Contains(w.Id))
                .FirstOrDefaultAsync(ct);

            if (entry is null)
                return null;

            // Schedule lock is already held, so taking the user lock keeps the global order (schedule -> user).
            lockScope.Add(await locks.AcquireAsync([LockKeys.User(entry.UserId)], ct));

            var package = entry.UserPackage;
            var skipReason =
                package.IsExpired(now) ? "Skipped: package expired before a slot opened up." :
                await rules.HasOverlappingBookingAsync(entry.UserId, schedule, ct) ? "Skipped: user has an overlapping booking." :
                null;

            if (skipReason is not null)
            {
                entry.MarkSkipped(now, skipReason);
                package.ReleaseReservation();
                CreditLedger.Record(db, package, CreditTransactionType.WaitlistRelease, now, waitlistEntry: entry);
                skipped.Add(entry.Id);
                continue;
            }

            schedule.TakeSlot();
            package.ConvertReservationToDebit();
            var booking = Booking.Create(entry.UserId, schedule, package, BookingSource.WaitlistPromotion, now);
            db.Bookings.Add(booking);
            entry.MarkPromoted(booking, now);
            CreditLedger.Record(db, package, CreditTransactionType.WaitlistPromotionDebit, now, booking, entry);

            return new Promotion(entry, booking);
        }

        return null;
    }
}
