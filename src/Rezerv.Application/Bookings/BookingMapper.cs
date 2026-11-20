using Rezerv.Domain.Entities;

namespace Rezerv.Application.Bookings;

internal static class BookingMapper
{
    public static BookingDto ToDto(this Booking b, TimetableSchedule s) => new(
        b.Id, b.UserId, b.ScheduleId, s.ClassName,
        DateTime.SpecifyKind(s.StartTime, DateTimeKind.Utc), DateTime.SpecifyKind(s.EndTime, DateTimeKind.Utc),
        b.UserPackageId, b.Status, b.Source, b.BookedAt, b.CancelledAt, b.CreditRefunded);
}
