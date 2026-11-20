using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rezerv.Domain.Entities;

namespace Rezerv.Infrastructure.Persistence.Configurations;

internal sealed class BusinessConfiguration : IEntityTypeConfiguration<Business>
{
    public void Configure(EntityTypeBuilder<Business> b)
    {
        b.ToTable("businesses");
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.Name).IsUnique();
    }
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Email).HasMaxLength(320).IsRequired();
        b.HasIndex(x => x.Email).IsUnique();
    }
}

internal sealed class InstructorConfiguration : IEntityTypeConfiguration<Instructor>
{
    public void Configure(EntityTypeBuilder<Instructor> b)
    {
        b.ToTable("instructors");
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.HasOne(x => x.Business).WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PackageConfiguration : IEntityTypeConfiguration<Package>
{
    public void Configure(EntityTypeBuilder<Package> b)
    {
        b.ToTable("packages", t =>
        {
            t.HasCheckConstraint("CK_packages_credits", "`Credits` > 0");
            t.HasCheckConstraint("CK_packages_validity", "`ValidityDays` > 0");
        });
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Price).HasPrecision(10, 2);
        b.HasOne(x => x.Business).WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class UserPackageConfiguration : IEntityTypeConfiguration<UserPackage>
{
    public void Configure(EntityTypeBuilder<UserPackage> b)
    {
        b.ToTable("user_packages", t =>
        {
            // Last line of defence: credits can never go negative or exceed the total,
            // and you can't reserve credits you don't have.
            t.HasCheckConstraint("CK_user_packages_remaining", "`RemainingCredits` >= 0 AND `RemainingCredits` <= `TotalCredits`");
            t.HasCheckConstraint("CK_user_packages_reserved", "`ReservedCredits` >= 0 AND `ReservedCredits` <= `RemainingCredits`");
        });

        b.Ignore(x => x.AvailableCredits);

        // Optimistic concurrency: UPDATE ... WHERE RemainingCredits = @old AND ReservedCredits = @old
        b.Property(x => x.RemainingCredits).IsConcurrencyToken();
        b.Property(x => x.ReservedCredits).IsConcurrencyToken();

        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Package).WithMany().HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Business).WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.UserId, x.BusinessId, x.ExpiresAt });
    }
}

internal sealed class TimetableScheduleConfiguration : IEntityTypeConfiguration<TimetableSchedule>
{
    public void Configure(EntityTypeBuilder<TimetableSchedule> b)
    {
        b.ToTable("timetable_schedules", t =>
        {
            t.HasCheckConstraint("CK_schedules_time", "`EndTime` > `StartTime`");
            t.HasCheckConstraint("CK_schedules_capacity", "`Capacity` > 0");
            // Even if every application-level guard failed, MySQL refuses to overbook.
            t.HasCheckConstraint("CK_schedules_booked", "`BookedCount` >= 0 AND `BookedCount` <= `Capacity`");
        });

        b.Property(x => x.ClassName).HasMaxLength(200).IsRequired();
        b.Ignore(x => x.RemainingSlots);
        b.Ignore(x => x.IsFull);

        b.Property(x => x.BookedCount).IsConcurrencyToken();

        b.HasOne(x => x.Business).WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Instructor).WithMany().HasForeignKey(x => x.InstructorId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.BusinessId, x.StartTime });
        b.HasIndex(x => x.StartTime);
    }
}

internal sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> b)
    {
        b.ToTable("bookings");
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).IsConcurrencyToken();

        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Schedule).WithMany().HasForeignKey(x => x.ScheduleId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.UserPackage).WithMany().HasForeignKey(x => x.UserPackageId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.ScheduleId, x.Status });
        b.HasIndex(x => new { x.UserId, x.Status });
    }
}

internal sealed class WaitlistEntryConfiguration : IEntityTypeConfiguration<WaitlistEntry>
{
    public void Configure(EntityTypeBuilder<WaitlistEntry> b)
    {
        b.ToTable("waitlist_entries");
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).IsConcurrencyToken();
        b.Property(x => x.ResolutionNote).HasMaxLength(500);

        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Schedule).WithMany().HasForeignKey(x => x.ScheduleId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.UserPackage).WithMany().HasForeignKey(x => x.UserPackageId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Booking).WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);

        // Covers the FIFO lookup: WHERE ScheduleId = ? AND Status = 'Waiting' ORDER BY JoinedAt, Id
        b.HasIndex(x => new { x.ScheduleId, x.Status, x.JoinedAt, x.Id });
        b.HasIndex(x => new { x.UserId, x.Status });
    }
}

internal sealed class CreditTransactionConfiguration : IEntityTypeConfiguration<CreditTransaction>
{
    public void Configure(EntityTypeBuilder<CreditTransaction> b)
    {
        b.ToTable("credit_transactions");
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(40);

        b.HasOne(x => x.UserPackage).WithMany().HasForeignKey(x => x.UserPackageId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Booking).WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.WaitlistEntry).WithMany().HasForeignKey(x => x.WaitlistEntryId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.UserPackageId, x.CreatedAt });
    }
}
