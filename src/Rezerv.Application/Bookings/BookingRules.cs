using Microsoft.EntityFrameworkCore;
using Rezerv.Application.Abstractions;
using Rezerv.Domain.Common;
using Rezerv.Domain.Entities;
using Rezerv.Domain.Enums;

namespace Rezerv.Application.Bookings;

/// <summary>
/// Checks that need the database (as opposed to the pure rules on the entities).
/// Shared by booking, waitlist join and waitlist promotion. Callers are expected
/// to hold the relevant locks.
/// </summary>
internal sealed class BookingRules(IAppDbContext db)
{
    public async Task EnsureNotAlreadyBookedOrWaitlistedAsync(int userId, int scheduleId, CancellationToken ct)
    {
        if (await db.Bookings.AnyAsync(b => b.UserId == userId && b.ScheduleId == scheduleId && b.Status == BookingStatus.Confirmed, ct))
            throw new DomainException(ErrorCodes.AlreadyBooked, "You have already booked this class.", ErrorKind.Conflict);

        if (await db.WaitlistEntries.AnyAsync(w => w.UserId == userId && w.ScheduleId == scheduleId && w.Status == WaitlistStatus.Waiting, ct))
            throw new DomainException(ErrorCodes.AlreadyWaitlisted, "You are already on the waitlist for this class.", ErrorKind.Conflict);
    }

    public Task<bool> HasOverlappingBookingAsync(int userId, TimetableSchedule schedule, CancellationToken ct) =>
        db.Bookings.AnyAsync(b =>
            b.UserId == userId &&
            b.Status == BookingStatus.Confirmed &&
            b.ScheduleId != schedule.Id &&
            b.Schedule.StartTime < schedule.EndTime &&
            schedule.StartTime < b.Schedule.EndTime, ct);

    public async Task EnsureNoOverlapAsync(int userId, TimetableSchedule schedule, CancellationToken ct)
    {
        if (await HasOverlappingBookingAsync(userId, schedule, ct))
            throw new DomainException(ErrorCodes.OverlappingBooking,
                "You already have a booking that overlaps with this class.", ErrorKind.Conflict);
    }

    /// <summary>
    /// Returns a tracked package the user can pay with: explicitly chosen, or picked
    /// automatically (valid, has credit, same business, expiring soonest).
    /// </summary>
    public async Task<UserPackage> ResolvePackageAsync(int userId, TimetableSchedule schedule, int? userPackageId, DateTime now, CancellationToken ct)
    {
        if (userPackageId is not null)
        {
            var chosen = await db.UserPackages.FirstOrDefaultAsync(p => p.Id == userPackageId, ct)
                         ?? throw DomainException.NotFound("User package", userPackageId);

            if (chosen.UserId != userId)
                throw new DomainException(ErrorCodes.PackageNotOwned, "This package does not belong to the user.", ErrorKind.Validation);

            chosen.EnsureUsableFor(schedule, now);
            chosen.EnsureHasAvailableCredit();
            return chosen;
        }

        var candidates = await db.UserPackages
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.ExpiresAt).ThenBy(p => p.Id)
            .ToListAsync(ct);

        var sameBusiness = candidates.Where(p => p.BusinessId == schedule.BusinessId).ToList();
        if (sameBusiness.Count == 0)
        {
            // Distinguish "only has packages for another studio" from "has nothing at all".
            throw candidates.Count > 0
                ? new DomainException(ErrorCodes.PackageBusinessMismatch,
                    "Your packages belong to a different business and cannot be used for this class.")
                : new DomainException(ErrorCodes.NoEligiblePackage,
                    "You don't have a package for this business. Purchase one first.");
        }

        var notExpired = sameBusiness.Where(p => !p.IsExpired(now)).ToList();
        if (notExpired.Count == 0)
            throw new DomainException(ErrorCodes.PackageExpired, "All of your packages for this business have expired.");

        return notExpired.FirstOrDefault(p => p.AvailableCredits >= 1)
               ?? throw new DomainException(ErrorCodes.InsufficientCredits,
                   "You don't have enough credits. Purchase a new package to book.");
    }
}
