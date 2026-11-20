using FluentAssertions;
using Rezerv.Domain.Common;
using Rezerv.Domain.Entities;

namespace Rezerv.UnitTests.Domain;

public class UserPackageTests
{
    private static readonly DateTime Now = new(2026, 6, 10, 9, 0, 0, DateTimeKind.Utc);

    private static UserPackage Package(int remaining = 10, int reserved = 0, int businessId = 1) => new()
    {
        BusinessId = businessId, TotalCredits = 10, RemainingCredits = remaining, ReservedCredits = reserved,
        ExpiresAt = Now.AddDays(10)
    };

    [Fact]
    public void Debit_takes_one_credit()
    {
        var p = Package();
        p.DebitCredit();
        p.RemainingCredits.Should().Be(9);
    }

    [Fact]
    public void Debit_with_no_available_credit_throws()
    {
        var p = Package(remaining: 1, reserved: 1);
        var act = () => p.DebitCredit();
        act.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.InsufficientCredits);
    }

    [Fact]
    public void Reserve_reduces_available_but_not_remaining()
    {
        var p = Package(remaining: 3);
        p.ReserveCredit();
        p.RemainingCredits.Should().Be(3);
        p.AvailableCredits.Should().Be(2);
    }

    [Fact]
    public void Converting_a_reservation_spends_the_credit()
    {
        var p = Package(remaining: 3, reserved: 1);
        p.ConvertReservationToDebit();
        p.RemainingCredits.Should().Be(2);
        p.ReservedCredits.Should().Be(0);
        p.AvailableCredits.Should().Be(2);
    }

    [Fact]
    public void Releasing_a_reservation_gives_the_credit_back_without_deduction()
    {
        var p = Package(remaining: 3, reserved: 1);
        p.ReleaseReservation();
        p.RemainingCredits.Should().Be(3);
        p.AvailableCredits.Should().Be(3);
    }

    [Fact]
    public void Refund_cannot_exceed_total()
    {
        var p = Package(remaining: 10);
        var act = () => p.RefundCredit();
        act.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.InvariantViolation);
    }

    [Fact]
    public void Package_is_expired_at_its_expiry_instant()
    {
        var p = Package();
        p.IsExpired(p.ExpiresAt.AddTicks(-1)).Should().BeFalse();
        p.IsExpired(p.ExpiresAt).Should().BeTrue();
    }

    [Fact]
    public void Package_from_another_business_cannot_be_used()
    {
        var p = Package(businessId: 1);
        var schedule = new TimetableSchedule { BusinessId = 2, StartTime = Now.AddDays(1), EndTime = Now.AddDays(1).AddHours(1) };
        var act = () => p.EnsureUsableFor(schedule, Now);
        act.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.PackageBusinessMismatch);
    }

    [Fact]
    public void Expired_package_cannot_be_used()
    {
        var p = Package();
        var schedule = new TimetableSchedule { BusinessId = 1 };
        var act = () => p.EnsureUsableFor(schedule, p.ExpiresAt.AddMinutes(1));
        act.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.PackageExpired);
    }

    [Fact]
    public void Purchase_assigns_all_credits_and_expiry()
    {
        var catalogue = new Package { Id = 3, BusinessId = 1, Credits = 10, ValidityDays = 30 };
        var up = UserPackage.Purchase(7, catalogue, Now);
        up.TotalCredits.Should().Be(10);
        up.RemainingCredits.Should().Be(10);
        up.ExpiresAt.Should().Be(Now.AddDays(30));
        up.BusinessId.Should().Be(1);
    }
}
