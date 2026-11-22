using System.ComponentModel.DataAnnotations;

namespace Rezerv.Application.Packages;

public record PackageDto(
    int PackageId,
    int BusinessId,
    string BusinessName,
    string Name,
    int Credits,
    int ValidityDays,
    decimal Price);

public record UserPackageDto(
    int UserPackageId,
    int PackageId,
    string PackageName,
    int BusinessId,
    string BusinessName,
    int TotalCredits,
    int RemainingCredits,
    int ReservedCredits,
    int AvailableCredits,
    DateTime PurchasedAt,
    DateTime ExpiresAt,
    bool IsExpired);

public record PurchasePackageRequest
{
    [Range(1, int.MaxValue)] public int UserId { get; init; }
    [Range(1, int.MaxValue)] public int PackageId { get; init; }
}
