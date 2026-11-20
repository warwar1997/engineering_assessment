using Microsoft.AspNetCore.Mvc;
using Rezerv.Application.Users;

namespace Rezerv.Api.Controllers;

[ApiController]
[Route("api/businesses")]
[Produces("application/json")]
public class BusinessesController(IDirectoryService directory) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<BusinessDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await directory.GetBusinessesAsync(ct));
}
