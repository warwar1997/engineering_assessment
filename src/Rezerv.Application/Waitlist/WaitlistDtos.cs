using System.ComponentModel.DataAnnotations;
using Rezerv.Domain.Enums;

namespace Rezerv.Application.Waitlist;

public record JoinWaitlistRequest
{
    [Range(1, int.MaxValue)] public int UserId { get; init; }
    [Range(1, int.MaxValue)] public int ScheduleId { get; init; }

    /// <summary>Optional, same auto-selection rules as booking.</summary>
    [Range(1, int.MaxValue)] public int? UserPackageId { get; init; }
}

public record LeaveWaitlistRequest
{
    [Range(1, int.MaxValue)] public int WaitlistEntryId { get; init; }
    [Range(1, int.MaxValue)] public int UserId { get; init; }
}

/// <param name="Position">1-based position in the queue while Waiting, otherwise null.</param>
public record WaitlistEntryDto(
    int WaitlistEntryId,
    int UserId,
    int ScheduleId,
    string ClassName,
    DateTime StartTime,
    int UserPackageId,
    WaitlistStatus Status,
    int? Position,
    DateTime JoinedAt,
    DateTime? ResolvedAt,
    string? ResolutionNote,
    int? BookingId);

public record JoinWaitlistResponse(WaitlistEntryDto Entry, int PackageRemainingCredits, int PackageAvailableCredits);

public record WaitlistExpiryResult(int ExpiredEntries, int AffectedSchedules);
