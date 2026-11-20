using Rezerv.Domain.Common;
using Rezerv.Domain.Enums;
using Rezerv.Domain.Policies;

namespace Rezerv.Domain.Entities;

public class Booking
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int ScheduleId { get; set; }
    public TimetableSchedule Schedule { get; set; } = null!;
    public int UserPackageId { get; set; }
    public UserPackage UserPackage { get; set; } = null!;

    public BookingStatus Status { get; set; }
    public BookingSource Source { get; set; }
    public DateTime BookedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public bool CreditRefunded { get; set; }

    public static Booking Create(int userId, TimetableSchedule schedule, UserPackage package, BookingSource source, DateTime now) =>
        new()
        {
            UserId = userId,
            ScheduleId = schedule.Id,
            Schedule = schedule,
            UserPackageId = package.Id,
            UserPackage = package,
            Status = BookingStatus.Confirmed,
            Source = source,
            BookedAt = now
        };

    /// <summary>
    /// Cancels the booking and returns whether a refund is due.
    /// The caller is responsible for actually refunding the package and freeing the slot.
    /// </summary>
    public bool Cancel(TimetableSchedule schedule, DateTime now)
    {
        if (Status != BookingStatus.Confirmed)
            throw new DomainException(ErrorCodes.BookingNotActive, "This booking is already cancelled.", ErrorKind.Conflict);

        schedule.EnsureNotStarted(now);

        Status = BookingStatus.Cancelled;
        CancelledAt = now;
        CreditRefunded = CancellationPolicy.IsRefundable(schedule.StartTime, now);
        return CreditRefunded;
    }
}
