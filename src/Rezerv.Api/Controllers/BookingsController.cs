using Microsoft.AspNetCore.Mvc;
using Rezerv.Application.Bookings;

namespace Rezerv.Api.Controllers;

[ApiController]
[Route("api/bookings")]
[Produces("application/json")]
public class BookingsController(IBookingService bookings) : ControllerBase
{
    /// <summary>Books a class and deducts 1 credit immediately.</summary>
    /// <remarks>
    /// Validates package business, expiry, credits, overlapping bookings and slot availability.
    /// If the class is full the API returns 409 `SCHEDULE_FULL`, unless `joinWaitlistIfFull` is true,
    /// in which case the user is waitlisted and 202 is returned.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType<BookClassResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<BookClassResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Book([FromBody] BookClassRequest request, CancellationToken ct)
    {
        var result = await bookings.BookAsync(request, ct);
        return result.Outcome == BookOutcome.Booked
            ? Created($"/api/users/{request.UserId}/bookings", result)
            : Accepted(result);
    }

    /// <summary>Cancels a booking. Refunds 1 credit if cancelled more than 4 hours before class start.</summary>
    /// <remarks>The freed slot is given to the first person on the waitlist automatically.</remarks>
    [HttpPost("cancel")]
    [ProducesResponseType<CancelBookingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Cancel([FromBody] CancelBookingRequest request, CancellationToken ct) =>
        Ok(await bookings.CancelAsync(request, ct));
}
