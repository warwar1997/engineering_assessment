using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Rezerv.Domain.Common;
using Rezerv.Domain.Enums;
using Rezerv.UnitTests.Support;

namespace Rezerv.UnitTests.Services;

public class WaitlistTests : ServiceTestBase
{
    [Fact]
    public async Task Joining_waitlist_of_a_class_with_free_slots_is_rejected()
    {
        var biz = Db.AddBusiness("A");
        var user = Db.AddUser("amy");
        Db.AddUserPackage(user, biz, 10, Now.AddDays(30));
        var schedule = Db.AddSchedule(biz, Now.AddDays(1), 5);

        var act = () => JoinWaitlistAsync(user.Id, schedule.Id);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(ErrorCodes.ScheduleNotFull);
    }

    [Fact]
    public async Task Cancellation_promotes_the_first_waitlisted_user_fifo_and_deducts_their_credit()
    {
        var biz = Db.AddBusiness("A");
        var booked = Db.AddUser("booked");
        var first = Db.AddUser("first");
        var second = Db.AddUser("second");
        Db.AddUserPackage(booked, biz, 10, Now.AddDays(30));
        var firstPkg = Db.AddUserPackage(first, biz, 10, Now.AddDays(30));
        var secondPkg = Db.AddUserPackage(second, biz, 10, Now.AddDays(30));
        var schedule = Db.AddSchedule(biz, Now.AddDays(1), capacity: 1);

        var booking = await BookAsync(booked.Id, schedule.Id);
        await JoinWaitlistAsync(first.Id, schedule.Id);
        Clock.Advance(TimeSpan.FromMinutes(1));
        var secondEntry = await JoinWaitlistAsync(second.Id, schedule.Id);
        secondEntry.Entry.Position.Should().Be(2);

        var cancel = await CancelAsync(booking.Booking!.BookingId, booked.Id);

        cancel.PromotedFromWaitlist.Should().NotBeNull();
        cancel.PromotedFromWaitlist!.UserId.Should().Be(first.Id);

        var p1 = Db.ReloadPackage(firstPkg.Id);
        p1.RemainingCredits.Should().Be(9);
        p1.ReservedCredits.Should().Be(0);

        var p2 = Db.ReloadPackage(secondPkg.Id);
        p2.RemainingCredits.Should().Be(10);
        p2.ReservedCredits.Should().Be(1); // still waiting

        Db.ReloadSchedule(schedule.Id).BookedCount.Should().Be(1);

        await using var ctx = Db.CreateContext();
        var promotedBooking = await ctx.Bookings.SingleAsync(b => b.UserId == first.Id);
        promotedBooking.Source.Should().Be(BookingSource.WaitlistPromotion);
    }

    [Fact]
    public async Task Promotion_skips_a_user_whose_package_expired_and_releases_their_reservation()
    {
        var biz = Db.AddBusiness("A");
        var booked = Db.AddUser("booked");
        var expiring = Db.AddUser("expiring");
        var next = Db.AddUser("next");
        Db.AddUserPackage(booked, biz, 10, Now.AddDays(30));
        var expiringPkg = Db.AddUserPackage(expiring, biz, 10, Now.AddHours(2));
        var nextPkg = Db.AddUserPackage(next, biz, 10, Now.AddDays(30));
        var schedule = Db.AddSchedule(biz, Now.AddDays(1), capacity: 1);

        var booking = await BookAsync(booked.Id, schedule.Id);
        await JoinWaitlistAsync(expiring.Id, schedule.Id);
        Clock.Advance(TimeSpan.FromMinutes(1));
        await JoinWaitlistAsync(next.Id, schedule.Id);

        Clock.Advance(TimeSpan.FromHours(3)); // first user's package has now expired
        var cancel = await CancelAsync(booking.Booking!.BookingId, booked.Id);

        cancel.PromotedFromWaitlist!.UserId.Should().Be(next.Id);
        var skipped = Db.ReloadPackage(expiringPkg.Id);
        skipped.ReservedCredits.Should().Be(0);
        skipped.RemainingCredits.Should().Be(10);
        Db.ReloadPackage(nextPkg.Id).RemainingCredits.Should().Be(9);

        await using var ctx = Db.CreateContext();
        (await ctx.WaitlistEntries.SingleAsync(w => w.UserId == expiring.Id)).Status.Should().Be(WaitlistStatus.Skipped);
    }

    [Fact]
    public async Task Waiting_users_get_their_reservation_released_when_the_class_ends()
    {
        var biz = Db.AddBusiness("A");
        var booked = Db.AddUser("booked");
        var waiting = Db.AddUser("waiting");
        Db.AddUserPackage(booked, biz, 10, Now.AddDays(30));
        var pkg = Db.AddUserPackage(waiting, biz, 10, Now.AddDays(30));
        var schedule = Db.AddSchedule(biz, Now.AddHours(1), capacity: 1);

        await BookAsync(booked.Id, schedule.Id);
        await JoinWaitlistAsync(waiting.Id, schedule.Id);
        Db.ReloadPackage(pkg.Id).AvailableCredits.Should().Be(9);

        // job runs before the class ends: nothing happens
        var (svc, ctx) = Waitlist();
        await using (ctx)
            (await svc.ExpireEndedAsync()).ExpiredEntries.Should().Be(0);

        Clock.Advance(TimeSpan.FromHours(2)); // class is over
        (svc, ctx) = Waitlist();
        await using (ctx)
            (await svc.ExpireEndedAsync()).ExpiredEntries.Should().Be(1);

        var reloaded = Db.ReloadPackage(pkg.Id);
        reloaded.RemainingCredits.Should().Be(10); // no deduction
        reloaded.ReservedCredits.Should().Be(0);

        // running again is a no-op
        (svc, ctx) = Waitlist();
        await using (ctx)
            (await svc.ExpireEndedAsync()).ExpiredEntries.Should().Be(0);
    }

    [Fact]
    public async Task Reserved_credit_cannot_be_spent_on_another_class()
    {
        var biz = Db.AddBusiness("A");
        var booked = Db.AddUser("booked");
        var user = Db.AddUser("single");
        Db.AddUserPackage(booked, biz, 10, Now.AddDays(30));
        Db.AddUserPackage(user, biz, 1, Now.AddDays(30)); // only one credit
        var full = Db.AddSchedule(biz, Now.AddDays(1), capacity: 1);
        var other = Db.AddSchedule(biz, Now.AddDays(2), capacity: 10);

        await BookAsync(booked.Id, full.Id);
        await JoinWaitlistAsync(user.Id, full.Id);
        var act = () => BookAsync(user.Id, other.Id);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(ErrorCodes.InsufficientCredits);
    }
}
