namespace Rezerv.Domain.Entities;

/// <summary>
/// A package product that a business sells (the catalogue item).
/// Buying one creates a <see cref="UserPackage"/>.
/// </summary>
public class Package
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public Business Business { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public int Credits { get; set; }
    public int ValidityDays { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
}
