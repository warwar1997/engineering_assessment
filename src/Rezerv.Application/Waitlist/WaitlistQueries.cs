using Microsoft.EntityFrameworkCore;
using Rezerv.Application.Abstractions;
using Rezerv.Domain.Entities;
using Rezerv.Domain.Enums;

namespace Rezerv.Application.Waitlist;

internal static class WaitlistQueries
{
    /// <summary>FIFO order: join time, with the auto-increment id as a tie breaker.</summary>
    public static IQueryable<WaitlistEntry> WaitingInOrder(this IQueryable<WaitlistEntry> source, int scheduleId) =>
        source.Where(w => w.ScheduleId == scheduleId && w.Status == WaitlistStatus.Waiting)
              .OrderBy(w => w.JoinedAt).ThenBy(w => w.Id);

    public static async Task<int?> PositionAsync(IAppDbContext db, WaitlistEntry entry, CancellationToken ct)
    {
        if (entry.Status != WaitlistStatus.Waiting)
            return null;

        var ahead = await db.WaitlistEntries.CountAsync(w =>
            w.ScheduleId == entry.ScheduleId &&
            w.Status == WaitlistStatus.Waiting &&
            (w.JoinedAt < entry.JoinedAt || (w.JoinedAt == entry.JoinedAt && w.Id < entry.Id)), ct);
        return ahead + 1;
    }

    public static WaitlistEntryDto ToDto(WaitlistEntry e, TimetableSchedule s, int? position) => new(
        e.Id, e.UserId, e.ScheduleId, s.ClassName, DateTime.SpecifyKind(s.StartTime, DateTimeKind.Utc),
        e.UserPackageId, e.Status, position, e.JoinedAt, e.ResolvedAt, e.ResolutionNote,
        e.BookingId ?? (e.Booking?.Id is > 0 ? e.Booking.Id : null));
}
