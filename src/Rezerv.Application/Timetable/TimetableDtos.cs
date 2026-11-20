namespace Rezerv.Application.Timetable;

/// <param name="AvailableSlots">Total slots for the class (capacity), matching the spec's example response.</param>
/// <param name="RemainingSlots">Slots still free right now (AvailableSlots - AttendanceCount).</param>
public record TimetableScheduleDto(
    int ScheduleId,
    string ClassName,
    string InstructorName,
    DateTime StartTime,
    DateTime EndTime,
    int AttendanceCount,
    int AvailableSlots,
    int RemainingSlots,
    bool IsFull,
    int WaitlistCount,
    int BusinessId,
    string BusinessName,
    bool CanBook,
    bool CanJoinWaitlist);
