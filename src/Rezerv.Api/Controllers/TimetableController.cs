using Microsoft.AspNetCore.Mvc;
using Rezerv.Application.Timetable;

namespace Rezerv.Api.Controllers;

[ApiController]
[Route("api/timetable")]
[Produces("application/json")]
public class TimetableController(ITimetableService timetable) : ControllerBase
{
    /// <summary>Lists timetable schedules, optionally filtered by business and/or date.</summary>
    /// <param name="businessId">Only schedules of this business.</param>
    /// <param name="date">Only schedules starting on this date (UTC), format yyyy-MM-dd.</param>
    /// <remarks>
    /// `availableSlots` is the class capacity (as in the spec example) and `remainingSlots` is what is still free.
    /// Use `canBook` / `canJoinWaitlist` to decide whether to call POST /api/bookings or POST /api/waitlist.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<TimetableScheduleDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int? businessId, [FromQuery] DateOnly? date, CancellationToken ct) =>
        Ok(await timetable.ListAsync(businessId, date, ct));

    /// <summary>Gets one schedule with live (uncached) availability.</summary>
    [HttpGet("{scheduleId:int}")]
    [ProducesResponseType<TimetableScheduleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(int scheduleId, CancellationToken ct) =>
        Ok(await timetable.GetAsync(scheduleId, ct));
}
