using Rezerv.Domain.Common;
using Rezerv.Domain.Enums;

namespace Rezerv.Domain.Entities;

public class WaitlistEntry
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int ScheduleId { get; set; }
    public TimetableSchedule Schedule { get; set; } = null!;
    public int UserPackageId { get; set; }
    public UserPackage UserPackage { get; set; } = null!;

    public WaitlistStatus Status { get; set; }
    public DateTime JoinedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public int? BookingId { get; set; }
    public Booking? Booking { get; set; }
    public string? ResolutionNote { get; set; }

    public static WaitlistEntry Create(int userId, TimetableSchedule schedule, UserPackage package, DateTime now) =>
        new()
        {
            UserId = userId,
            ScheduleId = schedule.Id,
            Schedule = schedule,
            UserPackageId = package.Id,
            UserPackage = package,
            Status = WaitlistStatus.Waiting,
            JoinedAt = now
        };

    public void MarkPromoted(Booking booking, DateTime now) => Resolve(WaitlistStatus.Promoted, now, "Promoted to booking.", booking);
    public void MarkCancelled(DateTime now) => Resolve(WaitlistStatus.Cancelled, now, "Left the waitlist.");
    public void MarkExpired(DateTime now) => Resolve(WaitlistStatus.Expired, now, "Class ended before a slot opened up.");
    public void MarkSkipped(DateTime now, string reason) => Resolve(WaitlistStatus.Skipped, now, reason);

    private void Resolve(WaitlistStatus status, DateTime now, string note, Booking? booking = null)
    {
        if (Status != WaitlistStatus.Waiting)
            throw new DomainException(ErrorCodes.WaitlistEntryNotActive, "This waitlist entry is no longer active.", ErrorKind.Conflict);

        Status = status;
        ResolvedAt = now;
        ResolutionNote = note;
        if (booking is not null)
            Booking = booking; // EF fills BookingId on save
    }
}
