using Cadence.Api.Infrastructure;
using Cadence.Api.Requests;
using Cadence.Application.Handlers;
using Microsoft.AspNetCore.Authorization;
using Cadence.Application.Common;
using Cadence.Application.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Cadence.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/coaching")]
[Produces("application/json")]
public sealed class CoachingController(
    GenerateCoachingReportHandler generateCoachingReport,
    GetLatestCoachingReportHandler getLatestCoachingReport,
    ICurrentAthlete currentAthlete) : ControllerBase
{
    private const int DefaultWeeks = 12;
    private const int MaxWeeks = 52;

    [HttpPost("reports")]
    [ProducesResponseType<CoachingReportDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CoachingReportDto>> Generate(
        // The web client posts this with no body at all, which [ApiController]
        // rejects as a missing payload unless an empty body is explicitly allowed.
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] GenerateCoachingReportRequest? request,
        CancellationToken cancellationToken)
    {
        var weeks = request?.Weeks ?? DefaultWeeks;
        if (weeks is < 1 or > MaxWeeks)
        {
            return this.ToProblem(Error.Validation($"'weeks' must be between 1 and {MaxWeeks}."));
        }

        var result = await generateCoachingReport.ExecuteAsync(
            currentAthlete.Id,
            weeks,
            request?.Refresh ?? false,
            cancellationToken);

        return result.ToActionResult(this, report => CreatedAtAction(nameof(Latest), null, report));
    }

    [HttpGet("reports/latest")]
    [ProducesResponseType<CoachingReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CoachingReportDto>> Latest(CancellationToken cancellationToken)
    {
        var result = await getLatestCoachingReport.ExecuteAsync(currentAthlete.Id, cancellationToken);
        return result.ToActionResult(this);
    }
}
