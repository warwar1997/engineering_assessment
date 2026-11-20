using Microsoft.AspNetCore.Mvc;
using Rezerv.Application.Waitlist;

namespace Rezerv.Api.Controllers;

[ApiController]
[Route("api/waitlist")]
[Produces("application/json")]
public class WaitlistController(IWaitlistService waitlist) : ControllerBase
{
    /// <summary>Joins the waitlist of a full class. Reserves (does not deduct) 1 credit.</summary>
    /// <remarks>
    /// The credit is deducted only when the user is promoted to a booking. If the class
    /// ends while the user is still waiting, the reservation is released.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType<JoinWaitlistResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Join([FromBody] JoinWaitlistRequest request, CancellationToken ct)
    {
        var result = await waitlist.JoinAsync(request, ct);
        return Created($"/api/users/{request.UserId}/waitlist", result);
    }

    /// <summary>Leaves a waitlist and releases the reserved credit.</summary>
    [HttpPost("leave")]
    [ProducesResponseType<WaitlistEntryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Leave([FromBody] LeaveWaitlistRequest request, CancellationToken ct) =>
        Ok(await waitlist.LeaveAsync(request, ct));

    /// <summary>
    /// Admin/testing helper: runs the waitlist expiry job now instead of waiting for the
    /// next scheduled (every minute) run.
    /// </summary>
    [HttpPost("expire")]
    [ProducesResponseType<WaitlistExpiryResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ExpireEnded(CancellationToken ct) =>
        Ok(await waitlist.ExpireEndedAsync(ct));
}
