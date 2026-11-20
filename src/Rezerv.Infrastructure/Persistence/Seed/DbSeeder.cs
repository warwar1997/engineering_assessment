using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rezerv.Domain.Entities;
using Rezerv.Domain.Enums;

namespace Rezerv.Infrastructure.Persistence.Seed;

/// <summary>
/// Seeds a dataset that exercises every business rule. All times are relative to "now"
/// so the classes are in the future whenever the app is first started.
/// Bookings and waitlist entries are created through the domain methods so that
/// BookedCount, credits and the ledger are consistent with each other.
///
/// See docs/SEED_DATA.md for the scenarios this enables.
/// </summary>
public class DbSeeder(AppDbContext db, TimeProvider clock, ILogger<DbSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (await db.Businesses.AnyAsync(ct))
        {
            logger.LogInformation("Database already seeded, skipping");
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var today = now.Date;
        var d1 = today.AddDays(1);
        var d2 = today.AddDays(2);
        var d3 = today.AddDays(3);

        // ---------- businesses & instructors ----------
        var rezerv = new Business { Name = "Rezerv Fitness", CreatedAt = now };
        var urban = new Business { Name = "Urban Flow Yoga", CreatedAt = now };
        db.Businesses.AddRange(rezerv, urban);

        var john = new Instructor { Business = rezerv, Name = "John Doe" };
        var jane = new Instructor { Business = rezerv, Name = "Jane Smith" };
        var mike = new Instructor { Business = rezerv, Name = "Mike Tan" };
        var sarah = new Instructor { Business = urban, Name = "Sarah Lee" };
        var daniel = new Instructor { Business = urban, Name = "Daniel Kim" };
        db.Instructors.AddRange(john, jane, mike, sarah, daniel);

        // ---------- package catalogue ----------
        var rz5 = new Package { Business = rezerv, Name = "Rezerv 5 Class Pack", Credits = 5, ValidityDays = 30, Price = 50m };
        var rz10 = new Package { Business = rezerv, Name = "Rezerv 10 Class Pack", Credits = 10, ValidityDays = 60, Price = 90m };
        var uf10 = new Package { Business = urban, Name = "Urban Flow 10 Class Pack", Credits = 10, ValidityDays = 60, Price = 100m };
        var ufDrop = new Package { Business = urban, Name = "Urban Flow Drop-in", Credits = 1, ValidityDays = 7, Price = 12m };
        db.Packages.AddRange(rz5, rz10, uf10, ufDrop);

        await db.SaveChangesAsync(ct); // need ids for the domain factory methods

        // ---------- users ----------
        string[] names =
        [
            "Alice Tan", "Bob Lim", "Chloe Ng", "David Wong", "Emma Lee",
            "Frank Ho", "Grace Koh", "Henry Goh", "Ivy Chua", "Jack Teo",
            "Karen Ong", "Leo Yeo", "Mia Sim", "Noah Chan", "Olivia Low"
        ];
        var users = names.Select(n => new User
        {
            Name = n,
            Email = $"{n.Split(' ')[0].ToLowerInvariant()}@example.com",
            CreatedAt = now.AddDays(-90)
        }).ToList();
        db.Users.AddRange(users);
        await db.SaveChangesAsync(ct);
        User U(int n) => users[n - 1];

        // ---------- user packages ----------
        var purchased = now.AddDays(-5);
        var pk = new Dictionary<int, UserPackage>();   // Rezerv Fitness package per user
        var pkUrban = new Dictionary<int, UserPackage>(); // Urban Flow package per user

        UserPackage Buy(User u, Package p, DateTime at)
        {
            var up = UserPackage.Purchase(u.Id, p, at);
            db.UserPackages.Add(up);
            Ledger(up, CreditTransactionType.Purchase, at);
            return up;
        }

        pk[1] = Buy(U(1), rz10, purchased);                                    // Alice: happy path
        var bobExpired = Buy(U(2), rz10, now.AddDays(-70));                    // Bob: expired 10 days ago
        Spend(bobExpired, 4, now.AddDays(-60));
        var chloeEmpty = Buy(U(3), rz5, now.AddDays(-10));                     // Chloe: valid but 0 credits
        Spend(chloeEmpty, 5, now.AddDays(-8));
        pkUrban[4] = Buy(U(4), uf10, purchased);                               // David: only Urban Flow
        pk[5] = Buy(U(5), rz10, purchased);                                    // Emma: both studios
        pkUrban[5] = Buy(U(5), uf10, purchased);
        foreach (var n in Enumerable.Range(6, 10))                             // Frank..Olivia
            pk[n] = Buy(U(n), rz10, purchased);
        pkUrban[13] = Buy(U(13), ufDrop, now.AddDays(-1));                     // Mia: 1-credit drop-in
        pkUrban[14] = Buy(U(14), ufDrop, now.AddDays(-1));                     // Noah: 1-credit drop-in

        await db.SaveChangesAsync(ct);

        // ---------- timetable ----------
        TimetableSchedule Class(Business b, Instructor i, string name, DateTime start, int minutes, int capacity)
        {
            var s = new TimetableSchedule
            {
                BusinessId = b.Id, Business = b, InstructorId = i.Id, Instructor = i,
                ClassName = name, StartTime = start, EndTime = start.AddMinutes(minutes), Capacity = capacity
            };
            db.TimetableSchedules.Add(s);
            return s;
        }

        // round "in ~2 hours" to a clean half hour so it reads nicely
        var soon = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute >= 30 ? 30 : 0, 0, DateTimeKind.Utc).AddHours(2).AddMinutes(30);
        var endedStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc).AddHours(-3);

        var s1 = Class(rezerv, john, "Morning Yoga", d1.AddHours(10), 60, 15);          // 12/15, mirrors the spec example
        var s2 = Class(rezerv, jane, "HIIT Blast", d1.AddHours(10.5), 60, 10);          // overlaps Morning Yoga
        var s3 = Class(rezerv, mike, "Spin Express", d1.AddHours(12), 45, 5);           // full + waitlist
        var s4 = Class(rezerv, jane, "Pilates Core", soon, 60, 8);                      // starts within 4h -> late cancel
        var s5 = Class(rezerv, mike, "Evening Boxing", endedStart, 60, 2);              // already ended, user still waitlisted
        var s6 = Class(rezerv, john, "Strength Lab", d2.AddHours(18), 60, 5);           // empty, 5 slots: concurrency test
        var s7 = Class(urban, sarah, "Vinyasa Flow", d1.AddHours(9), 60, 12);
        var s8 = Class(urban, daniel, "Power Yoga", d1.AddHours(18), 60, 3);            // full + waitlist
        var s9 = Class(urban, sarah, "Sunrise Barre", d2.AddHours(7), 60, 20);
        var s10 = Class(urban, daniel, "Yin & Meditation", d3.AddHours(19), 60, 1);     // full + waitlist
        var s11 = Class(rezerv, jane, "Core & Stretch", d3.AddHours(8), 45, 10);
        await db.SaveChangesAsync(ct);

        // ---------- bookings & waitlists ----------
        // Morning Yoga: 12 of 15
        foreach (var n in new[] { 1, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 })
            Book(U(n), s1, pk[n]);

        // Spin Express: full (5/5), Jack then Karen waiting
        foreach (var n in new[] { 5, 6, 7, 8, 9 })
            Book(U(n), s3, pk[n]);
        Waitlist(U(10), s3, pk[10], now.AddMinutes(-30));
        Waitlist(U(11), s3, pk[11], now.AddMinutes(-20));

        // Pilates Core (starts soon)
        Book(U(1), s4, pk[1]);
        Book(U(6), s4, pk[6]);

        // Evening Boxing (ended): full, Leo still waiting -> expiry job should release his credit
        Book(U(7), s5, pk[7], endedStart.AddDays(-1));
        Book(U(8), s5, pk[8], endedStart.AddDays(-1));
        Waitlist(U(12), s5, pk[12], endedStart.AddHours(-5));

        // Urban Flow
        Book(U(4), s7, pkUrban[4]);
        Book(U(5), s7, pkUrban[5]);

        Book(U(4), s8, pkUrban[4]);
        Book(U(5), s8, pkUrban[5]);
        Book(U(13), s8, pkUrban[13]);
        Waitlist(U(14), s8, pkUrban[14], now.AddMinutes(-10));

        Book(U(5), s10, pkUrban[5]);
        Waitlist(U(4), s10, pkUrban[4], now.AddMinutes(-5));

        Book(U(9), s11, pk[9]);
        Book(U(15), s11, pk[15]);

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Seed data created ({Users} users)", users.Count);

        // ----- local helpers -----
        void Book(User u, TimetableSchedule s, UserPackage p, DateTime? at = null)
        {
            var when = at ?? now.AddHours(-1);
            s.TakeSlot();
            p.DebitCredit();
            var booking = Booking.Create(u.Id, s, p, BookingSource.Direct, when);
            db.Bookings.Add(booking);
            Ledger(p, CreditTransactionType.BookingDebit, when, booking);
        }

        void Waitlist(User u, TimetableSchedule s, UserPackage p, DateTime at)
        {
            p.ReserveCredit();
            var entry = WaitlistEntry.Create(u.Id, s, p, at);
            db.WaitlistEntries.Add(entry);
            Ledger(p, CreditTransactionType.WaitlistReserve, at, waitlist: entry);
        }

        // credits used on historical classes that aren't part of the seed
        void Spend(UserPackage p, int credits, DateTime at)
        {
            for (var i = 0; i < credits; i++)
            {
                p.DebitCredit();
                Ledger(p, CreditTransactionType.BookingDebit, at);
            }
        }

        void Ledger(UserPackage p, CreditTransactionType type, DateTime at, Booking? booking = null, WaitlistEntry? waitlist = null)
        {
            var (rem, res) = type switch
            {
                CreditTransactionType.Purchase => (p.TotalCredits, 0),
                CreditTransactionType.BookingDebit => (-1, 0),
                CreditTransactionType.WaitlistReserve => (0, 1),
                _ => (0, 0)
            };
            db.CreditTransactions.Add(new CreditTransaction
            {
                UserPackage = p, Type = type, RemainingDelta = rem, ReservedDelta = res,
                Booking = booking, WaitlistEntry = waitlist, CreatedAt = at
            });
        }
    }
}
