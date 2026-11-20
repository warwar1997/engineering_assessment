using Microsoft.EntityFrameworkCore;
using Rezerv.Domain.Entities;

namespace Rezerv.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<Business> Businesses { get; }
    DbSet<User> Users { get; }
    DbSet<Instructor> Instructors { get; }
    DbSet<Package> Packages { get; }
    DbSet<UserPackage> UserPackages { get; }
    DbSet<TimetableSchedule> TimetableSchedules { get; }
    DbSet<Booking> Bookings { get; }
    DbSet<WaitlistEntry> WaitlistEntries { get; }
    DbSet<CreditTransaction> CreditTransactions { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
