using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rezerv.Application.Abstractions;
using Rezerv.Application.Bookings;
using Rezerv.Application.Common;
using Rezerv.Domain.Common;
using Rezerv.Domain.Entities;
using Rezerv.Domain.Enums;

namespace Rezerv.Application.Waitlist;

public interface IWaitlistService
{
    Task<JoinWaitlistResponse> JoinAsync(JoinWaitlistRequest request, CancellationToken ct = default);
    Task<WaitlistEntryDto> LeaveAsync(LeaveWaitlistRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<WaitlistEntryDto>> GetUserEntriesAsync(int userId, CancellationToken ct = default);

    /// <summary>
    /// Releases the reserved credit of everyone still waiting for a class that has ended.
    /// Run periodically by the background job; safe to run repeatedly.
    /// </summary>
    Task<WaitlistExpiryResult> ExpireEndedAsync(CancellationToken ct = default);
}

public class WaitlistService : IWaitlistService
{
    private const int ExpiryBatchSize = 200;

    private readonly IAppDbContext _db;
    private readonly IDistributedLockService _locks;
    private readonly ICacheService _cache;
    private readonly TimeProvider _clock;
    private readonly ILogger<WaitlistService> _logger;
    private readonly BookingRules _rules;

    public WaitlistService(IAppDbContext db, IDistributedLockService locks, ICacheService cache, TimeProvider clock,
        ILogger<WaitlistService> logger)
    {
        _db = db;
        _locks = locks;
        _cache = cache;
        _clock = clock;
        _logger = logger;
        _rules = new BookingRules(db);
    }

    public async Task<JoinWaitlistResponse> JoinAsync(JoinWaitlistRequest request, CancellationToken ct = default)
    {
        Guard.PositiveId(request.UserId, nameof(request.UserId));
        Guard.PositiveId(request.ScheduleId, nameof(request.ScheduleId));

        await using var _ = await _locks.AcquireAsync(
            [LockKeys.Schedule(request.ScheduleId), LockKeys.User(request.UserId)], ct);

        var now = _clock.UtcNow();

        if (!await _db.Users.AnyAsync(u => u.Id == request.UserId, ct))
            throw DomainException.NotFound("User", request.UserId);

        var schedule = await _db.TimetableSchedules.FirstOrDefaultAsync(s => s.Id == request.ScheduleId, ct)
                       ?? throw DomainException.NotFound("Timetable schedule", request.ScheduleId);

        schedule.EnsureNotStarted(now);

        if (!schedule.IsFull)
            throw new DomainException(ErrorCodes.ScheduleNotFull,
                "This class still has free slots. Book it directly via POST /api/bookings.", ErrorKind.Conflict);

        await _rules.EnsureNotAlreadyBookedOrWaitlistedAsync(request.UserId, schedule.Id, ct);
        var package = await _rules.ResolvePackageAsync(request.UserId, schedule, request.UserPackageId, now, ct);
        await _rules.EnsureNoOverlapAsync(request.UserId, schedule, ct);

        // Hold one credit so the user can't spend it elsewhere while waiting.
        // It is converted to a real debit on promotion, or released if the class ends.
        package.ReserveCredit();
        var entry = WaitlistEntry.Create(request.UserId, schedule, package, now);
        _db.WaitlistEntries.Add(entry);
        CreditLedger.Record(_db, package, CreditTransactionType.WaitlistReserve, now, waitlistEntry: entry);

        await _db.SaveChangesOrConflictAsync(ct);
        await _cache.BumpVersionAsync(CacheKeys.TimetableNamespace, ct);

        var position = await WaitlistQueries.PositionAsync(_db, entry, ct);
        return new JoinWaitlistResponse(WaitlistQueries.ToDto(entry, schedule, position),
            package.RemainingCredits, package.AvailableCredits);
    }

    public async Task<WaitlistEntryDto> LeaveAsync(LeaveWaitlistRequest request, CancellationToken ct = default)
    {
        Guard.PositiveId(request.WaitlistEntryId, nameof(request.WaitlistEntryId));
        Guard.PositiveId(request.UserId, nameof(request.UserId));

        var scheduleId = await _db.WaitlistEntries.AsNoTracking()
            .Where(w => w.Id == request.WaitlistEntryId && w.UserId == request.UserId)
            .Select(w => (int?)w.ScheduleId)
            .FirstOrDefaultAsync(ct)
            ?? throw DomainException.NotFound("Waitlist entry", request.WaitlistEntryId);

        // Schedule lock so we can't race with a promotion picking this same entry.
        await using var _ = await _locks.AcquireAsync(
            [LockKeys.Schedule(scheduleId), LockKeys.User(request.UserId)], ct);

        var now = _clock.UtcNow();
        var entry = await _db.WaitlistEntries
            .Include(w => w.UserPackage)
            .Include(w => w.Schedule)
            .FirstAsync(w => w.Id == request.WaitlistEntryId, ct);

        entry.MarkCancelled(now);
        entry.UserPackage.ReleaseReservation();
        CreditLedger.Record(_db, entry.UserPackage, CreditTransactionType.WaitlistRelease, now, waitlistEntry: entry);

        await _db.SaveChangesOrConflictAsync(ct);
        await _cache.BumpVersionAsync(CacheKeys.TimetableNamespace, ct);

        return WaitlistQueries.ToDto(entry, entry.Schedule, null);
    }

    public async Task<IReadOnlyList<WaitlistEntryDto>> GetUserEntriesAsync(int userId, CancellationToken ct = default)
    {
        if (!await _db.Users.AnyAsync(u => u.Id == userId, ct))
            throw DomainException.NotFound("User", userId);

        var entries = await _db.WaitlistEntries.AsNoTracking()
            .Include(w => w.Schedule)
            .Where(w => w.UserId == userId)
            .OrderByDescending(w => w.JoinedAt)
            .ToListAsync(ct);

        var result = new List<WaitlistEntryDto>(entries.Count);
        foreach (var e in entries)
            result.Add(WaitlistQueries.ToDto(e, e.Schedule, await WaitlistQueries.PositionAsync(_db, e, ct)));
        return result;
    }

    public async Task<WaitlistExpiryResult> ExpireEndedAsync(CancellationToken ct = default)
    {
        var now = _clock.UtcNow();
        var totalExpired = 0;
        var schedules = new HashSet<int>();

        while (true)
        {
            var batch = await _db.WaitlistEntries
                .Include(w => w.UserPackage)
                .Where(w => w.Status == WaitlistStatus.Waiting && w.Schedule.EndTime <= now)
                .OrderBy(w => w.Id)
                .Take(ExpiryBatchSize)
                .ToListAsync(ct);

            if (batch.Count == 0)
                break;

            foreach (var entry in batch)
            {
                // No credit is deducted - the reservation is simply handed back.
                entry.MarkExpired(now);
                entry.UserPackage.ReleaseReservation();
                CreditLedger.Record(_db, entry.UserPackage, CreditTransactionType.WaitlistRelease, now, waitlistEntry: entry);
                schedules.Add(entry.ScheduleId);
            }

            // If this collides with a concurrent write (e.g. the user leaving the waitlist at
            // the same moment) the batch fails as a whole and is retried on the next run.
            await _db.SaveChangesOrConflictAsync(ct);
            totalExpired += batch.Count;

            if (batch.Count < ExpiryBatchSize)
                break;
        }

        if (totalExpired > 0)
        {
            await _cache.BumpVersionAsync(CacheKeys.TimetableNamespace, ct);
            _logger.LogInformation("Expired {Count} waitlist entries across {Schedules} ended classes", totalExpired, schedules.Count);
        }

        return new WaitlistExpiryResult(totalExpired, schedules.Count);
    }
}
