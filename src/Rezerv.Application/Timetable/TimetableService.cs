using Microsoft.EntityFrameworkCore;
using Rezerv.Application.Abstractions;
using Rezerv.Application.Common;
using Rezerv.Domain.Common;
using Rezerv.Domain.Enums;

namespace Rezerv.Application.Timetable;

public interface ITimetableService
{
    Task<IReadOnlyList<TimetableScheduleDto>> ListAsync(int? businessId, DateOnly? date, CancellationToken ct = default);
    Task<TimetableScheduleDto> GetAsync(int scheduleId, CancellationToken ct = default);
}

public class TimetableService(IAppDbContext db, ICacheService cache, TimeProvider clock) : ITimetableService
{
    // Short TTL: attendance numbers change often. Every booking/cancel bumps the
    // timetable version, so in practice entries are invalidated well before the TTL.
    private static readonly TimeSpan ListTtl = TimeSpan.FromSeconds(60);

    public async Task<IReadOnlyList<TimetableScheduleDto>> ListAsync(int? businessId, DateOnly? date, CancellationToken ct = default)
    {
        var version = await cache.GetVersionAsync(CacheKeys.TimetableNamespace, ct);
        var rows = await cache.GetOrCreateAsync(
            CacheKeys.Timetable(version, businessId, date),
            () => QueryAsync(businessId, date, null, ct),
            ListTtl, ct);

        // CanBook / CanJoinWaitlist depend on the current time, so they are computed after the cache.
        var now = clock.UtcNow();
        return rows.Select(r => r.WithFlags(now)).ToList();
    }

    public async Task<TimetableScheduleDto> GetAsync(int scheduleId, CancellationToken ct = default)
    {
        var rows = await QueryAsync(null, null, scheduleId, ct);
        var row = rows.SingleOrDefault() ?? throw DomainException.NotFound("Timetable schedule", scheduleId);
        return row.WithFlags(clock.UtcNow());
    }

    private async Task<IReadOnlyList<Row>> QueryAsync(int? businessId, DateOnly? date, int? scheduleId, CancellationToken ct)
    {
        var query = db.TimetableSchedules.AsNoTracking();

        if (scheduleId is not null)
            query = query.Where(s => s.Id == scheduleId);
        if (businessId is not null)
            query = query.Where(s => s.BusinessId == businessId);
        if (date is not null)
        {
            // Dates are interpreted in UTC (see README assumptions).
            var from = date.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var to = from.AddDays(1);
            query = query.Where(s => s.StartTime >= from && s.StartTime < to);
        }

        return await query
            .OrderBy(s => s.StartTime).ThenBy(s => s.Id)
            .Select(s => new Row(
                s.Id, s.ClassName, s.Instructor.Name, s.StartTime, s.EndTime,
                s.BookedCount, s.Capacity,
                db.WaitlistEntries.Count(w => w.ScheduleId == s.Id && w.Status == WaitlistStatus.Waiting),
                s.BusinessId, s.Business.Name))
            .ToListAsync(ct);
    }

    // Cached shape (no time-dependent fields).
    public record Row(
        int ScheduleId, string ClassName, string InstructorName, DateTime StartTime, DateTime EndTime,
        int AttendanceCount, int Capacity, int WaitlistCount, int BusinessId, string BusinessName)
    {
        public TimetableScheduleDto WithFlags(DateTime now)
        {
            var isFull = AttendanceCount >= Capacity;
            var notStarted = now < StartTime;
            return new TimetableScheduleDto(
                ScheduleId, ClassName, InstructorName,
                DateTime.SpecifyKind(StartTime, DateTimeKind.Utc), DateTime.SpecifyKind(EndTime, DateTimeKind.Utc),
                AttendanceCount, Capacity, Math.Max(0, Capacity - AttendanceCount), isFull, WaitlistCount,
                BusinessId, BusinessName,
                CanBook: notStarted && !isFull,
                CanJoinWaitlist: notStarted && isFull);
        }
    }
}
