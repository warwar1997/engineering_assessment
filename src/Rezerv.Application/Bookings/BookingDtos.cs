using System.ComponentModel.DataAnnotations;
using Rezerv.Application.Waitlist;
using Rezerv.Domain.Enums;

namespace Rezerv.Application.Bookings;

public record BookClassRequest
{
    [Range(1, int.MaxValue)] public int UserId { get; init; }
    [Range(1, int.MaxValue)] public int ScheduleId { get; init; }

    /// <summary>
    /// Package to pay with. Optional: if omitted, the user's valid package for the
    /// class's business that expires soonest is used.
    /// </summary>
    [Range(1, int.MaxValue)] public int? UserPackageId { get; init; }

    /// <summary>If the class is full, put the user on the waitlist instead of returning 409.</summary>
    public bool JoinWaitlistIfFull { get; init; }
}

public record CancelBookingRequest
{
    [Range(1, int.MaxValue)] public int BookingId { get; init; }

    /// <summary>The user cancelling. Must own the booking (stand-in for an authenticated user).</summary>
    [Range(1, int.MaxValue)] public int UserId { get; init; }
}

public enum BookOutcome
{
    Booked = 1,
    Waitlisted = 2
}

public record BookingDto(
    int BookingId,
    int UserId,
    int ScheduleId,
    string ClassName,
    DateTime StartTime,
    DateTime EndTime,
    int UserPackageId,
    BookingStatus Status,
    BookingSource Source,
    DateTime BookedAt,
    DateTime? CancelledAt,
    bool CreditRefunded);

public record BookClassResponse(
    BookOutcome Outcome,
    BookingDto? Booking,
    WaitlistEntryDto? WaitlistEntry,
    int PackageRemainingCredits,
    int PackageAvailableCredits);

public record CancelBookingResponse(
    BookingDto Booking,
    bool CreditRefunded,
    string Message,
    int PackageRemainingCredits,
    PromotionDto? PromotedFromWaitlist);

public record PromotionDto(int WaitlistEntryId, int UserId, int BookingId);
