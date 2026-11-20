using Microsoft.EntityFrameworkCore;
using Rezerv.Application.Abstractions;

namespace Rezerv.Application.Users;

public record UserDto(int UserId, string Name, string Email);
public record BusinessDto(int BusinessId, string Name);

/// <summary>Read-only lookups so the seeded data is easy to explore from Swagger.</summary>
public interface IDirectoryService
{
    Task<IReadOnlyList<UserDto>> GetUsersAsync(CancellationToken ct = default);
    Task<IReadOnlyList<BusinessDto>> GetBusinessesAsync(CancellationToken ct = default);
}

public class DirectoryService(IAppDbContext db) : IDirectoryService
{
    public async Task<IReadOnlyList<UserDto>> GetUsersAsync(CancellationToken ct = default) =>
        await db.Users.AsNoTracking().OrderBy(u => u.Id).Select(u => new UserDto(u.Id, u.Name, u.Email)).ToListAsync(ct);

    public async Task<IReadOnlyList<BusinessDto>> GetBusinessesAsync(CancellationToken ct = default) =>
        await db.Businesses.AsNoTracking().OrderBy(b => b.Id).Select(b => new BusinessDto(b.Id, b.Name)).ToListAsync(ct);
}
