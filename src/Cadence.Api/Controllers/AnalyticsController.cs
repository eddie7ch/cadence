using Cadence.Api.Infrastructure;
using Cadence.Application.Handlers;
using Microsoft.AspNetCore.Authorization;
using Cadence.Application.Common;
using Cadence.Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Cadence.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/analytics")]
[Produces("application/json")]
public sealed class AnalyticsController(
    GetTrendsHandler getTrends,
    ICurrentAthlete currentAthlete) : ControllerBase
{
    private const int DefaultWeeks = 12;
    private const int MaxWeeks = 104;

    [HttpGet("trends")]
    [ProducesResponseType<TrendsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TrendsDto>> Trends(
        [FromQuery] int weeks = DefaultWeeks,
        CancellationToken cancellationToken = default)
    {
        if (weeks is < 1 or > MaxWeeks)
        {
            return this.ToProblem(Error.Validation($"'weeks' must be between 1 and {MaxWeeks}."));
        }

        // The handler takes an explicit window rather than a week count: a
        // cache key built from concrete dates stays stable across the request that
        // straddles midnight, which "the last 12 weeks" does not.
        var to = DateTimeOffset.UtcNow;
        var from = to.AddDays(-7 * weeks);

        var result = await getTrends.ExecuteAsync(currentAthlete.Id, from, to, cancellationToken);
        return result.ToActionResult(this);
    }
}
