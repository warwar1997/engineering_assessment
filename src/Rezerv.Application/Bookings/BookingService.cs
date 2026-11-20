using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rezerv.Application.Abstractions;
using Rezerv.Application.Common;
using Rezerv.Application.Waitlist;
using Rezerv.Domain.Common;
using Rezerv.Domain.Entities;
using Rezerv.Domain.Enums;

namespace Rezerv.Application.Bookings;

public interface IBookingService
{
    Task<BookClassResponse> BookAsync(BookClassRequest request, CancellationToken ct = default);
    Task<CancelBookingResponse> CancelAsync(CancelBookingRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<BookingDto>> GetUserBookingsAsync(int userId, CancellationToken ct = default);
}

public class BookingService : IBookingService
{
    private readonly IAppDbContext _db;
    private readonly IDistributedLockService _locks;
    private readonly ICacheService _cache;
    private readonly TimeProvider _clock;
    private readonly ILogger<BookingService> _logger;
    private readonly BookingRules _rules;
    private readonly WaitlistPromoter _promoter;

    public BookingService(IAppDbContext db, IDistributedLockService locks, ICacheService cache, TimeProvider clock,
        ILogger<BookingService> logger)
    {
        _db = db;
        _locks = locks;
        _cache = cache;
        _clock = clock;
        _logger = logger;
        _rules = new BookingRules(db);
        _promoter = new WaitlistPromoter(db, locks, _rules);
    }

    public async Task<BookClassResponse> BookAsync(BookClassRequest request, CancellationToken ct = default)
    {
        Guard.PositiveId(request.UserId, nameof(request.UserId));
        Guard.PositiveId(request.ScheduleId, nameof(request.ScheduleId));

        // Lock order: schedule, then user. The schedule lock serialises everyone competing
        // for the same slots; the user lock stops one user from double-spending credits or
        // booking two overlapping classes in parallel requests.
        await using var _ = await _locks.AcquireAsync(
            [LockKeys.Schedule(request.ScheduleId), LockKeys.User(request.UserId)], ct);

        var now = _clock.UtcNow();

        if (!await _db.Users.AnyAsync(u => u.Id == request.UserId, ct))
            throw DomainException.NotFound("User", request.UserId);

        var schedule = await _db.TimetableSchedules.FirstOrDefaultAsync(s => s.Id == request.ScheduleId, ct)
                       ?? throw DomainException.NotFound("Timetable schedule", request.ScheduleId);

        schedule.EnsureNotStarted(now);
        await _rules.EnsureNotAlreadyBookedOrWaitlistedAsync(request.UserId, schedule.Id, ct);
        var package = await _rules.ResolvePackageAsync(request.UserId, schedule, request.UserPackageId, now, ct);
        await _rules.EnsureNoOverlapAsync(request.UserId, schedule, ct);

        if (schedule.IsFull)
        {
            if (!request.JoinWaitlistIfFull)
                throw new DomainException(ErrorCodes.ScheduleFull,
                    "This class is full. Join the waitlist via POST /api/waitlist (or resend with joinWaitlistIfFull=true).",
                    ErrorKind.Conflict);

            var entry = WaitlistEntry.Create(request.UserId, schedule, package, now);
            package.ReserveCredit();
            _db.WaitlistEntries.Add(entry);
            CreditLedger.Record(_db, package, CreditTransactionType.WaitlistReserve, now, waitlistEntry: entry);
            await _db.SaveChangesOrConflictAsync(ct);
            await _cache.BumpVersionAsync(CacheKeys.TimetableNamespace, ct);

            var position = await WaitlistQueries.PositionAsync(_db, entry, ct);
            _logger.LogInformation("User {UserId} waitlisted for schedule {ScheduleId} at position {Position}",
                request.UserId, schedule.Id, position);

            return new BookClassResponse(BookOutcome.Waitlisted, null,
                WaitlistQueries.ToDto(entry, schedule, position),
                package.RemainingCredits, package.AvailableCredits);
        }

        schedule.TakeSlot();
        package.DebitCredit();
        var booking = Booking.Create(request.UserId, schedule, package, BookingSource.Direct, now);
        _db.Bookings.Add(booking);
        CreditLedger.Record(_db, package, CreditTransactionType.BookingDebit, now, booking);

        // Single SaveChanges = single DB transaction. Schedule.BookedCount and the package
        // credit columns are concurrency tokens, so if the lock was somehow bypassed the
        // UPDATE matches 0 rows and nothing is written.
        await _db.SaveChangesOrConflictAsync(ct);
        await _cache.BumpVersionAsync(CacheKeys.TimetableNamespace, ct);

        _logger.LogInformation("User {UserId} booked schedule {ScheduleId} (booking {BookingId}), {Booked}/{Capacity}",
            request.UserId, schedule.Id, booking.Id, schedule.BookedCount, schedule.Capacity);

        return new BookClassResponse(BookOutcome.Booked, booking.ToDto(schedule), null,
            package.RemainingCredits, package.AvailableCredits);
    }

    public async Task<CancelBookingResponse> CancelAsync(CancelBookingRequest request, CancellationToken ct = default)
    {
        Guard.PositiveId(request.BookingId, nameof(request.BookingId));
        Guard.PositiveId(request.UserId, nameof(request.UserId));

        // Need the schedule id before we know which lock to take.
        var scheduleId = await _db.Bookings.AsNoTracking()
            .Where(b => b.Id == request.BookingId && b.UserId == request.UserId)
            .Select(b => (int?)b.ScheduleId)
            .FirstOrDefaultAsync(ct)
            ?? throw DomainException.NotFound("Booking", request.BookingId);

        await using var locks = new LockScope();
        locks.Add(await _locks.AcquireAsync([LockKeys.Schedule(scheduleId)], ct));

        var now = _clock.UtcNow();
        var booking = await _db.Bookings
            .Include(b => b.Schedule)
            .Include(b => b.UserPackage)
            .FirstAsync(b => b.Id == request.BookingId, ct);
        var schedule = booking.Schedule;

        var refund = booking.Cancel(schedule, now);
        schedule.FreeSlot();
        if (refund)
        {
            booking.UserPackage.RefundCredit();
            CreditLedger.Record(_db, booking.UserPackage, CreditTransactionType.CancellationRefund, now, booking);
        }

        // The slot is free again (refund or not), so hand it to the next person in line.
        var promoted = await _promoter.PromoteNextAsync(schedule, now, locks, ct);

        await _db.SaveChangesOrConflictAsync(ct);
        await _cache.BumpVersionAsync(CacheKeys.TimetableNamespace, ct);

        _logger.LogInformation("Booking {BookingId} cancelled (refund: {Refund}). Promoted waitlist entry: {Promoted}",
            booking.Id, refund, promoted?.Entry.Id);

        var message = refund
            ? "Booking cancelled. 1 credit has been refunded."
            : $"Booking cancelled less than {Domain.Policies.CancellationPolicy.RefundWindow.TotalHours:0} hours before class start. No credit refunded.";

        return new CancelBookingResponse(
            booking.ToDto(schedule), refund, message, booking.UserPackage.RemainingCredits,
            promoted is null ? null : new PromotionDto(promoted.Entry.Id, promoted.Entry.UserId, promoted.Booking.Id));
    }

    public async Task<IReadOnlyList<BookingDto>> GetUserBookingsAsync(int userId, CancellationToken ct = default)
    {
        if (!await _db.Users.AnyAsync(u => u.Id == userId, ct))
            throw DomainException.NotFound("User", userId);

        var bookings = await _db.Bookings.AsNoTracking()
            .Include(b => b.Schedule)
            .Where(b => b.UserId == userId)
            .OrderBy(b => b.Schedule.StartTime)
            .ToListAsync(ct);

        return bookings.Select(b => b.ToDto(b.Schedule)).ToList();
    }
}
