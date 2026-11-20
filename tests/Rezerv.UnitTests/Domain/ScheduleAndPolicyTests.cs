using FluentAssertions;
using Rezerv.Domain.Common;
using Rezerv.Domain.Entities;
using Rezerv.Domain.Enums;
using Rezerv.Domain.Policies;

namespace Rezerv.UnitTests.Domain;

public class ScheduleAndPolicyTests
{
    private static readonly DateTime Start = new(2026, 6, 10, 10, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(4 * 60 + 1, true)]   // 4h01m before -> refund
    [InlineData(4 * 60, false)]      // exactly 4h  -> "within 4 hours", no refund
    [InlineData(30, false)]
    public void Refund_only_if_cancelled_more_than_four_hours_before(int minutesBefore, bool expected) =>
        CancellationPolicy.IsRefundable(Start, Start.AddMinutes(-minutesBefore)).Should().Be(expected);

    [Fact]
    public void Taking_a_slot_on_a_full_class_throws()
    {
        var s = new TimetableSchedule { Capacity = 2, BookedCount = 2 };
        var act = () => s.TakeSlot();
        act.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.ScheduleFull);
    }

    [Theory]
    [InlineData(10, 30, 11, 30, true)]  // partial overlap
    [InlineData(9, 0, 12, 0, true)]     // contains
    [InlineData(11, 0, 12, 0, false)]   // back to back is fine
    [InlineData(8, 0, 10, 0, false)]
    public void Overlap_detection(int sh, int sm, int eh, int em, bool overlaps)
    {
        var s = new TimetableSchedule { StartTime = Start, EndTime = Start.AddHours(1) };
        var d = Start.Date;
        s.OverlapsWith(d.AddHours(sh).AddMinutes(sm), d.AddHours(eh).AddMinutes(em)).Should().Be(overlaps);
    }

    [Fact]
    public void Cannot_cancel_after_class_started()
    {
        var s = new TimetableSchedule { StartTime = Start, EndTime = Start.AddHours(1), Capacity = 5, BookedCount = 1 };
        var b = new Booking { Status = BookingStatus.Confirmed };
        var act = () => b.Cancel(s, Start.AddMinutes(1));
        act.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.ClassAlreadyStarted);
    }

    [Fact]
    public void Waitlist_entry_can_only_be_resolved_once()
    {
        var e = new WaitlistEntry { Status = WaitlistStatus.Waiting };
        e.MarkExpired(Start);
        var act = () => e.MarkCancelled(Start);
        act.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.WaitlistEntryNotActive);
    }
}
