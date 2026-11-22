using Microsoft.EntityFrameworkCore;
using Rezerv.Application.Abstractions;
using Rezerv.Application.Common;
using Rezerv.Domain.Common;
using Rezerv.Domain.Entities;
using Rezerv.Domain.Enums;

namespace Rezerv.Application.Packages;

public interface IPackageService
{
    Task<IReadOnlyList<PackageDto>> GetCatalogAsync(int? businessId, CancellationToken ct = default);
    Task<UserPackageDto> PurchaseAsync(PurchasePackageRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<UserPackageDto>> GetUserPackagesAsync(int userId, CancellationToken ct = default);
}

public class PackageService(IAppDbContext db, ICacheService cache, TimeProvider clock) : IPackageService
{
    // The catalogue changes rarely (no write API here), so a longer TTL is fine.
    private static readonly TimeSpan CatalogTtl = TimeSpan.FromMinutes(10);

    public Task<IReadOnlyList<PackageDto>> GetCatalogAsync(int? businessId, CancellationToken ct = default) =>
        cache.GetOrCreateAsync(CacheKeys.PackageCatalog(businessId), async () =>
        {
            var query = db.Packages.AsNoTracking().Where(p => p.IsActive);
            if (businessId is not null)
                query = query.Where(p => p.BusinessId == businessId);

            IReadOnlyList<PackageDto> result = await query
                .OrderBy(p => p.BusinessId).ThenBy(p => p.Credits)
                .Select(p => new PackageDto(p.Id, p.BusinessId, p.Business.Name, p.Name, p.Credits, p.ValidityDays, p.Price))
                .ToListAsync(ct);
            return result;
        }, CatalogTtl, ct);

    public async Task<UserPackageDto> PurchaseAsync(PurchasePackageRequest request, CancellationToken ct = default)
    {
        Guard.PositiveId(request.UserId, nameof(request.UserId));
        Guard.PositiveId(request.PackageId, nameof(request.PackageId));

        if (!await db.Users.AnyAsync(u => u.Id == request.UserId, ct))
            throw DomainException.NotFound("User", request.UserId);

        var package = await db.Packages.Include(p => p.Business).FirstOrDefaultAsync(p => p.Id == request.PackageId, ct)
                      ?? throw DomainException.NotFound("Package", request.PackageId);

        // Mock payment: assume the charge succeeded. In a real system this would be
        // created in a "PendingPayment" state and activated by the payment webhook.
        var now = clock.UtcNow();
        var userPackage = UserPackage.Purchase(request.UserId, package, now);
        userPackage.Package = package;
        userPackage.Business = package.Business;

        db.UserPackages.Add(userPackage);
        CreditLedger.Record(db, userPackage, CreditTransactionType.Purchase, now);
        await db.SaveChangesAsync(ct);

        return ToDto(userPackage, now);
    }

    public async Task<IReadOnlyList<UserPackageDto>> GetUserPackagesAsync(int userId, CancellationToken ct = default)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId, ct))
            throw DomainException.NotFound("User", userId);

        var now = clock.UtcNow();
        var packages = await db.UserPackages.AsNoTracking()
            .Include(p => p.Package).Include(p => p.Business)
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.ExpiresAt)
            .ToListAsync(ct);

        return packages.Select(p => ToDto(p, now)).ToList();
    }

    internal static UserPackageDto ToDto(UserPackage p, DateTime now) => new(
        p.Id, p.PackageId, p.Package.Name, p.BusinessId, p.Business.Name,
        p.TotalCredits, p.RemainingCredits, p.ReservedCredits, p.AvailableCredits,
        p.PurchasedAt, p.ExpiresAt, p.IsExpired(now));
}
