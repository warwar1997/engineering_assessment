using Microsoft.AspNetCore.Mvc;
using Rezerv.Application.Packages;

namespace Rezerv.Api.Controllers;

[ApiController]
[Route("api/packages")]
[Produces("application/json")]
public class PackagesController(IPackageService packages) : ControllerBase
{
    /// <summary>Lists the packages available for purchase.</summary>
    /// <param name="businessId">Optional: only packages sold by this business.</param>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PackageDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int? businessId, CancellationToken ct) =>
        Ok(await packages.GetCatalogAsync(businessId, ct));

    /// <summary>Mock purchase: assigns the package's credits to the user. No payment is taken.</summary>
    [HttpPost("purchase")]
    [ProducesResponseType<UserPackageDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Purchase([FromBody] PurchasePackageRequest request, CancellationToken ct)
    {
        var result = await packages.PurchaseAsync(request, ct);
        return Created($"/api/users/{request.UserId}/packages", result);
    }
}
