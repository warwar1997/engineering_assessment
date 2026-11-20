using Microsoft.AspNetCore.Mvc;
using Rezerv.Application.Bookings;
using Rezerv.Application.Packages;
using Rezerv.Application.Users;
using Rezerv.Application.Waitlist;

namespace Rezerv.Api.Controllers;

/// <summary>
/// There is no authentication in this exercise, so user-scoped data is exposed by user id.
/// </summary>
[ApiController]
[Route("api/users")]
[Produces("application/json")]
public class UsersController(
    IDirectoryService directory,
    IPackageService packages,
    IBookingService bookings,
    IWaitlistService waitlist) : ControllerBase
{
    /// <summary>Lists the (seeded) users.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<UserDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await directory.GetUsersAsync(ct));

    /// <summary>Packages owned by the user, with remaining / reserved / available credits.</summary>
    [HttpGet("{userId:int}/packages")]
    [ProducesResponseType<IReadOnlyList<UserPackageDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Packages(int userId, CancellationToken ct) =>
        Ok(await packages.GetUserPackagesAsync(userId, ct));

    /// <summary>The user's bookings (confirmed and cancelled).</summary>
    [HttpGet("{userId:int}/bookings")]
    [ProducesResponseType<IReadOnlyList<BookingDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Bookings(int userId, CancellationToken ct) =>
        Ok(await bookings.GetUserBookingsAsync(userId, ct));

    /// <summary>The user's waitlist entries, including current queue position.</summary>
    [HttpGet("{userId:int}/waitlist")]
    [ProducesResponseType<IReadOnlyList<WaitlistEntryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Waitlist(int userId, CancellationToken ct) =>
        Ok(await waitlist.GetUserEntriesAsync(userId, ct));
}
