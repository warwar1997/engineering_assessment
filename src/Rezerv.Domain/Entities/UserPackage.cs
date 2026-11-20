using Rezerv.Domain.Common;

namespace Rezerv.Domain.Entities;

/// <summary>
/// A package owned by a user. Credits live here.
///
/// RemainingCredits = credits not yet spent.
/// ReservedCredits  = credits held for waitlist entries (not spent yet, but not usable either).
/// AvailableCredits = what the user can actually spend right now.
/// </summary>
public class UserPackage
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int PackageId { get; set; }
    public Package Package { get; set; } = null!;
    public int BusinessId { get; set; }
    public Business Business { get; set; } = null!;

    public int TotalCredits { get; set; }
    public int RemainingCredits { get; set; }
    public int ReservedCredits { get; set; }

    public DateTime PurchasedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    public int AvailableCredits => RemainingCredits - ReservedCredits;

    public bool IsExpired(DateTime now) => now >= ExpiresAt;

    public static UserPackage Purchase(int userId, Package package, DateTime now)
    {
        if (!package.IsActive)
            throw new DomainException(ErrorCodes.Validation, "This package is no longer on sale.", ErrorKind.Validation);

        return new UserPackage
        {
            UserId = userId,
            PackageId = package.Id,
            BusinessId = package.BusinessId,
            TotalCredits = package.Credits,
            RemainingCredits = package.Credits,
            ReservedCredits = 0,
            PurchasedAt = now,
            ExpiresAt = now.AddDays(package.ValidityDays)
        };
    }

    /// <summary>Checks expiry and business match. Does not check credits.</summary>
    public void EnsureUsableFor(TimetableSchedule schedule, DateTime now)
    {
        if (BusinessId != schedule.BusinessId)
            throw new DomainException(ErrorCodes.PackageBusinessMismatch,
                "This package belongs to a different business and cannot be used for this class.");

        if (IsExpired(now))
            throw new DomainException(ErrorCodes.PackageExpired,
                $"This package expired on {ExpiresAt:yyyy-MM-dd HH:mm} UTC.");
    }

    public void EnsureHasAvailableCredit()
    {
        if (AvailableCredits < 1)
            throw new DomainException(ErrorCodes.InsufficientCredits,
                "Not enough credits on this package.");
    }

    /// <summary>Direct booking: spend one credit immediately.</summary>
    public void DebitCredit()
    {
        EnsureHasAvailableCredit();
        RemainingCredits--;
    }

    /// <summary>Joining a waitlist: hold one credit so it can't be spent elsewhere.</summary>
    public void ReserveCredit()
    {
        EnsureHasAvailableCredit();
        ReservedCredits++;
    }

    /// <summary>Waitlist promotion: the held credit is now actually spent.</summary>
    public void ConvertReservationToDebit()
    {
        if (ReservedCredits < 1 || RemainingCredits < 1)
            throw new DomainException(ErrorCodes.InvariantViolation,
                "Package has no reserved credit to convert.", ErrorKind.Conflict);
        ReservedCredits--;
        RemainingCredits--;
    }

    /// <summary>Waitlist left / expired / skipped: give the held credit back.</summary>
    public void ReleaseReservation()
    {
        if (ReservedCredits < 1)
            throw new DomainException(ErrorCodes.InvariantViolation,
                "Package has no reserved credit to release.", ErrorKind.Conflict);
        ReservedCredits--;
    }

    public void RefundCredit()
    {
        if (RemainingCredits >= TotalCredits)
            throw new DomainException(ErrorCodes.InvariantViolation,
                "Refund would exceed the package's total credits.", ErrorKind.Conflict);
        RemainingCredits++;
    }
}
