using Rezerv.Domain.Common;

namespace Rezerv.Domain.Entities;

public class TimetableSchedule
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public Business Business { get; set; } = null!;
    public int InstructorId { get; set; }
    public Instructor Instructor { get; set; } = null!;

    public string ClassName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }

    /// <summary>Maximum attendees ("available slots" in the spec).</summary>
    public int Capacity { get; set; }

    /// <summary>
    /// Denormalised count of confirmed bookings. Kept on the row so that the
    /// slot check is a single-row read and can be protected with an
    /// optimistic concurrency token.
    /// </summary>
    public int BookedCount { get; set; }

    public int RemainingSlots => Math.Max(0, Capacity - BookedCount);
    public bool IsFull => BookedCount >= Capacity;

    public bool HasStarted(DateTime now) => now >= StartTime;
    public bool HasEnded(DateTime now) => now >= EndTime;

    public bool OverlapsWith(DateTime start, DateTime end) => StartTime < end && start < EndTime;

    public void EnsureNotStarted(DateTime now)
    {
        if (HasStarted(now))
            throw new DomainException(ErrorCodes.ClassAlreadyStarted, "This class has already started.");
    }

    public void TakeSlot()
    {
        if (IsFull)
            throw new DomainException(ErrorCodes.ScheduleFull, "This class is full.", ErrorKind.Conflict);
        BookedCount++;
    }

    public void FreeSlot()
    {
        if (BookedCount <= 0)
            throw new DomainException(ErrorCodes.InvariantViolation, "Booked count cannot go below zero.", ErrorKind.Conflict);
        BookedCount--;
    }
}
