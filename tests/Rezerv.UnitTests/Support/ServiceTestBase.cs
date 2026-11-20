using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Rezerv.Application.Abstractions;
using Rezerv.Application.Bookings;
using Rezerv.Application.Waitlist;
using Rezerv.Infrastructure.Persistence;

namespace Rezerv.UnitTests.Support;

public abstract class ServiceTestBase : IDisposable
{
    protected static readonly DateTime Now = new(2026, 6, 10, 6, 0, 0, DateTimeKind.Utc);

    protected readonly TestDatabase Db = new();
    protected readonly FakeTimeProvider Clock = new(new DateTimeOffset(Now));
    protected IDistributedLockService Locks = new InMemoryLockService();

    protected (BookingService Service, AppDbContext Context) Booking()
    {
        var ctx = Db.CreateContext();
        return (new BookingService(ctx, Locks, new PassThroughCache(), Clock, NullLogger<BookingService>.Instance), ctx);
    }

    protected (WaitlistService Service, AppDbContext Context) Waitlist()
    {
        var ctx = Db.CreateContext();
        return (new WaitlistService(ctx, Locks, new PassThroughCache(), Clock, NullLogger<WaitlistService>.Instance), ctx);
    }

    protected async Task<BookClassResponse> BookAsync(int userId, int scheduleId, int? packageId = null, bool joinWaitlist = false)
    {
        var (svc, ctx) = Booking();
        await using (ctx)
            return await svc.BookAsync(new BookClassRequest
            {
                UserId = userId, ScheduleId = scheduleId, UserPackageId = packageId, JoinWaitlistIfFull = joinWaitlist
            });
    }

    protected async Task<CancelBookingResponse> CancelAsync(int bookingId, int userId)
    {
        var (svc, ctx) = Booking();
        await using (ctx)
            return await svc.CancelAsync(new CancelBookingRequest { BookingId = bookingId, UserId = userId });
    }

    protected async Task<JoinWaitlistResponse> JoinWaitlistAsync(int userId, int scheduleId)
    {
        var (svc, ctx) = Waitlist();
        await using (ctx)
            return await svc.JoinAsync(new JoinWaitlistRequest { UserId = userId, ScheduleId = scheduleId });
    }

    public void Dispose()
    {
        Db.Dispose();
        GC.SuppressFinalize(this);
    }
}
