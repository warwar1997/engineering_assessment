using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Rezerv.Application.Bookings;
using Rezerv.Domain.Common;
using Rezerv.Domain.Enums;
using Rezerv.UnitTests.Support;

namespace Rezerv.UnitTests.Services;

public class BookingServiceTests : ServiceTestBase
{
    [Fact]
    public async Task Booking_deducts_one_credit_and_takes_a_slot()
    {
        var biz = Db.AddBusiness("A");
        var user = Db.AddUser("alice");
        var pkg = Db.AddUserPackage(user, biz, credits: 10, expiresAt: Now.AddDays(30));
        var schedule = Db.AddSchedule(biz, Now.AddDays(1), capacity: 15);

        var result = await BookAsync(user.Id, schedule.Id);

        result.Outcome.Should().Be(BookOutcome.Booked);
        result.PackageRemainingCredits.Should().Be(9);
        Db.ReloadPackage(pkg.Id).RemainingCredits.Should().Be(9);
        Db.ReloadSchedule(schedule.Id).BookedCount.Should().Be(1);
    }

    [Fact]
    public async Task Package_from_another_business_is_rejected()
    {
        var a = Db.AddBusiness("A");
        var b = Db.AddBusiness("B");
        var user = Db.AddUser("bob");
        Db.AddUserPackage(user, a, 10, Now.AddDays(30));
        var scheduleB = Db.AddSchedule(b, Now.AddDays(1), 10);

        var act = () => BookAsync(user.Id, scheduleB.Id);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(ErrorCodes.PackageBusinessMismatch);
    }

    [Fact]
    public async Task Expired_package_is_rejected()
    {
        var biz = Db.AddBusiness("A");
        var user = Db.AddUser("carl");
        Db.AddUserPackage(user, biz, 10, expiresAt: Now.AddDays(-1));
        var schedule = Db.AddSchedule(biz, Now.AddDays(1), 10);

        var act = () => BookAsync(user.Id, schedule.Id);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(ErrorCodes.PackageExpired);
    }

    [Fact]
    public async Task No_credits_left_is_rejected()
    {
        var biz = Db.AddBusiness("A");
        var user = Db.AddUser("dana");
        Db.AddUserPackage(user, biz, 5, Now.AddDays(30), remaining: 0);
        var schedule = Db.AddSchedule(biz, Now.AddDays(1), 10);

        var act = () => BookAsync(user.Id, schedule.Id);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(ErrorCodes.InsufficientCredits);
    }

    [Fact]
    public async Task Overlapping_booking_is_rejected_but_back_to_back_is_allowed()
    {
        var biz = Db.AddBusiness("A");
        var user = Db.AddUser("eve");
        Db.AddUserPackage(user, biz, 10, Now.AddDays(30));
        var start = Now.AddDays(1);
        var yoga = Db.AddSchedule(biz, start, 10);                       // 10:00-11:00
        var hiit = Db.AddSchedule(biz, start.AddMinutes(30), 10);        // 10:30-11:30
        var spin = Db.AddSchedule(biz, start.AddMinutes(60), 10);        // 11:00-12:00

        await BookAsync(user.Id, yoga.Id);
        var overlapping = () => BookAsync(user.Id, hiit.Id);

        (await overlapping.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(ErrorCodes.OverlappingBooking);
        (await BookAsync(user.Id, spin.Id)).Outcome.Should().Be(BookOutcome.Booked);
    }

    [Fact]
    public async Task Booking_the_same_class_twice_is_rejected()
    {
        var biz = Db.AddBusiness("A");
        var user = Db.AddUser("finn");
        Db.AddUserPackage(user, biz, 10, Now.AddDays(30));
        var schedule = Db.AddSchedule(biz, Now.AddDays(1), 10);

        await BookAsync(user.Id, schedule.Id);
        var again = () => BookAsync(user.Id, schedule.Id);

        (await again.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(ErrorCodes.AlreadyBooked);
    }

    [Fact]
    public async Task Full_class_returns_schedule_full_or_waitlists_when_asked()
    {
        var biz = Db.AddBusiness("A");
        var first = Db.AddUser("gina");
        var second = Db.AddUser("hank");
        Db.AddUserPackage(first, biz, 10, Now.AddDays(30));
        var pkg = Db.AddUserPackage(second, biz, 10, Now.AddDays(30));
        var schedule = Db.AddSchedule(biz, Now.AddDays(1), capacity: 1);
        await BookAsync(first.Id, schedule.Id);

        var act = () => BookAsync(second.Id, schedule.Id);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(ErrorCodes.ScheduleFull);

        var waitlisted = await BookAsync(second.Id, schedule.Id, joinWaitlist: true);
        waitlisted.Outcome.Should().Be(BookOutcome.Waitlisted);
        waitlisted.WaitlistEntry!.Position.Should().Be(1);

        // credit reserved, not deducted
        var reloaded = Db.ReloadPackage(pkg.Id);
        reloaded.RemainingCredits.Should().Be(10);
        reloaded.ReservedCredits.Should().Be(1);
    }

    [Fact]
    public async Task Without_explicit_package_the_soonest_expiring_valid_one_is_used()
    {
        var biz = Db.AddBusiness("A");
        var user = Db.AddUser("ivy");
        Db.AddUserPackage(user, biz, 10, Now.AddDays(-1));               // expired
        Db.AddUserPackage(user, biz, 10, Now.AddDays(2), remaining: 0);  // no credits
        var soon = Db.AddUserPackage(user, biz, 10, Now.AddDays(5));
        var later = Db.AddUserPackage(user, biz, 10, Now.AddDays(50));
        var schedule = Db.AddSchedule(biz, Now.AddDays(1), 10);

        var result = await BookAsync(user.Id, schedule.Id);

        result.Booking!.UserPackageId.Should().Be(soon.Id);
        Db.ReloadPackage(later.Id).RemainingCredits.Should().Be(10);
    }

    [Fact]
    public async Task Cancelling_more_than_four_hours_before_refunds_the_credit()
    {
        var biz = Db.AddBusiness("A");
        var user = Db.AddUser("jack");
        var pkg = Db.AddUserPackage(user, biz, 10, Now.AddDays(30));
        var schedule = Db.AddSchedule(biz, Now.AddHours(5), 10);
        var booking = await BookAsync(user.Id, schedule.Id);

        var result = await CancelAsync(booking.Booking!.BookingId, user.Id);

        result.CreditRefunded.Should().BeTrue();
        Db.ReloadPackage(pkg.Id).RemainingCredits.Should().Be(10);
        Db.ReloadSchedule(schedule.Id).BookedCount.Should().Be(0);
    }

    [Fact]
    public async Task Cancelling_within_four_hours_does_not_refund()
    {
        var biz = Db.AddBusiness("A");
        var user = Db.AddUser("kate");
        var pkg = Db.AddUserPackage(user, biz, 10, Now.AddDays(30));
        var schedule = Db.AddSchedule(biz, Now.AddHours(3), 10);
        var booking = await BookAsync(user.Id, schedule.Id);

        var result = await CancelAsync(booking.Booking!.BookingId, user.Id);

        result.CreditRefunded.Should().BeFalse();
        Db.ReloadPackage(pkg.Id).RemainingCredits.Should().Be(9);
        Db.ReloadSchedule(schedule.Id).BookedCount.Should().Be(0); // the slot is still released
    }

    [Fact]
    public async Task Users_cannot_cancel_someone_elses_booking()
    {
        var biz = Db.AddBusiness("A");
        var owner = Db.AddUser("leo");
        var other = Db.AddUser("max");
        Db.AddUserPackage(owner, biz, 10, Now.AddDays(30));
        var schedule = Db.AddSchedule(biz, Now.AddDays(1), 10);
        var booking = await BookAsync(owner.Id, schedule.Id);

        var act = () => CancelAsync(booking.Booking!.BookingId, other.Id);

        (await act.Should().ThrowAsync<DomainException>()).Which.Kind.Should().Be(ErrorKind.NotFound);
    }

    [Fact]
    public async Task Ten_concurrent_bookings_for_five_slots_never_overbook()
    {
        var biz = Db.AddBusiness("A");
        var schedule = Db.AddSchedule(biz, Now.AddDays(1), capacity: 5);
        var users = Enumerable.Range(1, 10).Select(i => Db.AddUser($"user{i}")).ToList();
        foreach (var u in users)
            Db.AddUserPackage(u, biz, 10, Now.AddDays(30));

        var results = await Task.WhenAll(users.Select(u => Task.Run(async () =>
        {
            try
            {
                await BookAsync(u.Id, schedule.Id);
                return "booked";
            }
            catch (DomainException ex)
            {
                return ex.Code;
            }
        })));

        results.Count(r => r == "booked").Should().Be(5);
        results.Count(r => r == ErrorCodes.ScheduleFull).Should().Be(5);

        Db.ReloadSchedule(schedule.Id).BookedCount.Should().Be(5);
        await using var ctx = Db.CreateContext();
        (await ctx.Bookings.CountAsync(b => b.ScheduleId == schedule.Id && b.Status == BookingStatus.Confirmed)).Should().Be(5);
    }

    [Fact]
    public async Task Concurrency_token_rejects_a_stale_write_even_without_the_redis_lock()
    {
        // Simulates the lock being lost: two requests both read "1 slot left".
        var biz = Db.AddBusiness("A");
        var schedule = Db.AddSchedule(biz, Now.AddDays(1), capacity: 1);

        await using var a = Db.CreateContext();
        await using var b = Db.CreateContext();
        var copyA = await a.TimetableSchedules.SingleAsync(s => s.Id == schedule.Id);
        var copyB = await b.TimetableSchedules.SingleAsync(s => s.Id == schedule.Id);

        copyA.TakeSlot();
        copyB.TakeSlot();
        await a.SaveChangesAsync();
        var stale = () => b.SaveChangesAsync();

        await stale.Should().ThrowAsync<DbUpdateConcurrencyException>();
        Db.ReloadSchedule(schedule.Id).BookedCount.Should().Be(1);
    }

    [Fact]
    public async Task Without_any_lock_the_database_still_prevents_overbooking()
    {
        Locks = new NoLockService();
        var biz = Db.AddBusiness("A");
        var schedule = Db.AddSchedule(biz, Now.AddDays(1), capacity: 3);
        var users = Enumerable.Range(1, 8).Select(i => Db.AddUser($"nolock{i}")).ToList();
        foreach (var u in users)
            Db.AddUserPackage(u, biz, 10, Now.AddDays(30));

        await Task.WhenAll(users.Select(u => Task.Run(async () =>
        {
            try { await BookAsync(u.Id, schedule.Id); }
            catch (DomainException) { /* full or concurrent update - both fine */ }
            catch (DbUpdateException) { /* SQLite busy under contention - also a rejected write */ }
        })));

        await using var ctx = Db.CreateContext();
        var confirmed = await ctx.Bookings.CountAsync(x => x.ScheduleId == schedule.Id && x.Status == BookingStatus.Confirmed);
        confirmed.Should().BeLessThanOrEqualTo(3);
        Db.ReloadSchedule(schedule.Id).BookedCount.Should().Be(confirmed);
    }
}
