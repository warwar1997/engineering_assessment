using Microsoft.EntityFrameworkCore;
using Rezerv.Application.Abstractions;
using Rezerv.Domain.Common;

namespace Rezerv.Application.Common;

internal static class DbContextExtensions
{
    /// <summary>
    /// Saves and translates an optimistic concurrency failure into a 409.
    /// With the Redis lock in place this should be rare; it is the safety net for
    /// when the lock is lost (expiry, Redis failover) or a write path doesn't take a lock.
    /// </summary>
    public static async Task SaveChangesOrConflictAsync(this IAppDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException(ErrorCodes.ConcurrentUpdate,
                "The data was changed by another request. Please try again.", ErrorKind.Conflict);
        }
    }
}
