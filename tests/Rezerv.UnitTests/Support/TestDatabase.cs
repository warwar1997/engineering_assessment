using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Rezerv.Domain.Entities;
using Rezerv.Infrastructure.Persistence;

namespace Rezerv.UnitTests.Support;

/// <summary>
/// A throwaway SQLite file database. A file (rather than :memory:) lets several
/// DbContexts with their own connections run in parallel, which the concurrency tests need.
/// The model is the same one used against MySQL, including concurrency tokens and check constraints.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"rezerv-tests-{Guid.NewGuid():N}.db");

    public TestDatabase()
    {
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = _path, DefaultTimeout = 30 }.ToString())
            .Options;
        return new AppDbContext(options);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { /* best effort */ }
    }

    // ----- data builders -----

    public Business AddBusiness(string name)
    {
        using var db = CreateContext();
        var b = new Business { Name = name, CreatedAt = DateTime.UtcNow };
        db.Businesses.Add(b);
        db.SaveChanges();
        return b;
    }

    public User AddUser(string name)
    {
        using var db = CreateContext();
        var u = new User { Name = name, Email = $"{name.ToLowerInvariant()}-{Guid.NewGuid():N}@test.local", CreatedAt = DateTime.UtcNow };
        db.Users.Add(u);
        db.SaveChanges();
        return u;
    }

    public UserPackage AddUserPackage(User user, Business business, int credits, DateTime expiresAt, int remaining = -1)
    {
        using var db = CreateContext();
        var package = new Package { BusinessId = business.Id, Name = $"{credits} pack", Credits = credits, ValidityDays = 30, Price = 10 };
        db.Packages.Add(package);
        db.SaveChanges();

        var up = new UserPackage
        {
            UserId = user.Id, PackageId = package.Id, BusinessId = business.Id,
            TotalCredits = credits, RemainingCredits = remaining < 0 ? credits : remaining,
            PurchasedAt = expiresAt.AddDays(-30), ExpiresAt = expiresAt
        };
        db.UserPackages.Add(up);
        db.SaveChanges();
        return up;
    }

    public TimetableSchedule AddSchedule(Business business, DateTime start, int capacity, int minutes = 60, string name = "Yoga")
    {
        using var db = CreateContext();
        var instructor = new Instructor { BusinessId = business.Id, Name = "John Doe" };
        db.Instructors.Add(instructor);
        db.SaveChanges();

        var s = new TimetableSchedule
        {
            BusinessId = business.Id, InstructorId = instructor.Id, ClassName = name,
            StartTime = start, EndTime = start.AddMinutes(minutes), Capacity = capacity
        };
        db.TimetableSchedules.Add(s);
        db.SaveChanges();
        return s;
    }

    public UserPackage ReloadPackage(int id)
    {
        using var db = CreateContext();
        return db.UserPackages.AsNoTracking().Single(p => p.Id == id);
    }

    public TimetableSchedule ReloadSchedule(int id)
    {
        using var db = CreateContext();
        return db.TimetableSchedules.AsNoTracking().Single(s => s.Id == id);
    }
}
