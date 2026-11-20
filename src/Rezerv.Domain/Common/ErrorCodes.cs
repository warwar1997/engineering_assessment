namespace Rezerv.Domain.Common;

public static class ErrorCodes
{
    public const string NotFound = "NOT_FOUND";
    public const string Validation = "VALIDATION_FAILED";

    // packages
    public const string PackageExpired = "PACKAGE_EXPIRED";
    public const string InsufficientCredits = "INSUFFICIENT_CREDITS";
    public const string PackageBusinessMismatch = "PACKAGE_BUSINESS_MISMATCH";
    public const string PackageNotOwned = "PACKAGE_NOT_OWNED";
    public const string NoEligiblePackage = "NO_ELIGIBLE_PACKAGE";

    // schedules / bookings
    public const string ScheduleFull = "SCHEDULE_FULL";
    public const string ScheduleNotFull = "SCHEDULE_NOT_FULL";
    public const string ClassAlreadyStarted = "CLASS_ALREADY_STARTED";
    public const string OverlappingBooking = "OVERLAPPING_BOOKING";
    public const string AlreadyBooked = "ALREADY_BOOKED";
    public const string BookingNotActive = "BOOKING_NOT_ACTIVE";

    // waitlist
    public const string AlreadyWaitlisted = "ALREADY_WAITLISTED";
    public const string WaitlistEntryNotActive = "WAITLIST_ENTRY_NOT_ACTIVE";

    // infrastructure-ish, but surfaced to the client
    public const string ResourceBusy = "RESOURCE_BUSY";
    public const string ConcurrentUpdate = "CONCURRENT_UPDATE";
    public const string InvariantViolation = "INVARIANT_VIOLATION";
}
