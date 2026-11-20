using Rezerv.Domain.Enums;

namespace Rezerv.Domain.Entities;

/// <summary>
/// Append-only ledger of everything that happened to a package's credits.
/// RemainingCredits on <see cref="UserPackage"/> is the fast path; this table is the audit trail.
/// </summary>
public class CreditTransaction
{
    public long Id { get; set; }
    public int UserPackageId { get; set; }
    public UserPackage UserPackage { get; set; } = null!;
    public CreditTransactionType Type { get; set; }

    /// <summary>Change to RemainingCredits (e.g. -1 for a booking).</summary>
    public int RemainingDelta { get; set; }

    /// <summary>Change to ReservedCredits (e.g. +1 when joining a waitlist).</summary>
    public int ReservedDelta { get; set; }

    public int? BookingId { get; set; }
    public int? WaitlistEntryId { get; set; }
    public DateTime CreatedAt { get; set; }

    // navigation properties so the ids are filled in on the same SaveChanges
    public Booking? Booking { get; set; }
    public WaitlistEntry? WaitlistEntry { get; set; }
}
