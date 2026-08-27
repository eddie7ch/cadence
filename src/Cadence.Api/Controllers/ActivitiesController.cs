using Cadence.Api.Infrastructure;
using Cadence.Application.Abstractions;
using Cadence.Application.Common;
using Cadence.Application.Contracts;
using Cadence.Application.Handlers;
using Cadence.Domain.Activities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cadence.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/activities")]
[Produces("application/json")]
public sealed class ActivitiesController(
    ListActivitiesHandler listActivities,
    ImportActivityHandler importActivity,
    GetActivityDetailHandler getActivityDetail,
    GetTimeSeriesHandler getTimeSeries,
    DeleteActivityHandler deleteActivity,
    FindNearbyActivitiesHandler findNearby,
    ActivityProcessingQueue processingQueue,
    ICurrentAthlete currentAthlete,
    ILogger<ActivitiesController> logger) : ControllerBase
{
    public const long MaxUploadBytes = 50L * 1024 * 1024;

    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".gpx", ".fit", ".tcx" };

    [HttpGet]
    [ProducesResponseType<PagedDto<ActivitySummaryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedDto<ActivitySummaryDto>>> List(
        [FromQuery] Sport? sport,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] double? minDistance,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (from is { } start && to is { } end && start > end)
        {
            return this.ToProblem(Error.Validation("'from' must not be later than 'to'."));
        }

        if (minDistance is < 0)
        {
            return this.ToProblem(Error.Validation("'minDistance' must not be negative."));
        }

        var query = new ActivityQuery
        {
            Sport = sport,
            From = from,
            To = to,
            MinimumDistanceMeters = minDistance,

            // Clamped rather than rejected: an out-of-range page size is a client
            // bug that should still return a usable page, but an unbounded one is
            // a way to ask the database for every row an athlete owns.
            Page = Math.Max(page, 1),
            PageSize = Math.Clamp(pageSize, 1, 100),
        };

        var result = await listActivities.ExecuteAsync(currentAthlete.Id, query, cancellationToken);
        return result.ToActionResult(this);
    }

    /// <summary>
    /// Accepts the file and returns immediately. Decoding a FIT file and
    /// embedding several thousand samples takes seconds, which is far too long to
    /// hold a request open, so the work is handed to a background worker and the
    /// client polls the activity's status.
    /// </summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    [ProducesResponseType<ActivitySummaryDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType)]
    public async Task<ActionResult<ActivitySummaryDto>> Upload(
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return this.ToProblem(Error.Validation("A non-empty 'file' part is required."));
        }

        string extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(extension) || !SupportedExtensions.Contains(extension))
        {
            return Problem(
                detail: $"'{file.FileName}' is not a supported activity file. Upload a .gpx, .fit or .tcx file.",
                statusCode: StatusCodes.Status415UnsupportedMediaType,
                title: "Unsupported media type");
        }

        if (file.Length > MaxUploadBytes)
        {
            return Problem(
                detail: $"The file is larger than the {MaxUploadBytes / (1024 * 1024)} MB upload limit.",
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "Payload too large");
        }

        await using Stream content = file.OpenReadStream();
        var result = await importActivity.ExecuteAsync(
            currentAthlete.Id,
            file.FileName,
            content,
            cancellationToken);

        if (!result.IsSuccess)
        {
            return this.ToProblem(result.Error!);
        }

        ActivitySummaryDto summary = result.Value!;

        // Only queue work that has some. Re-uploading a file the athlete already
        // has returns the existing activity, and reprocessing it would burn a
        // worker slot to arrive at the same rows.
        if (summary.Status == nameof(ActivityStatus.Pending))
        {
            await processingQueue.EnqueueAsync(summary.Id, cancellationToken);
            logger.LogInformation("Queued activity {ActivityId} for processing.", summary.Id);
        }

        return Accepted($"/api/v1/activities/{summary.Id}", summary);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ActivityDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ActivityDetailDto>> Detail(Guid id, CancellationToken cancellationToken)
    {
        var result = await getActivityDetail.ExecuteAsync(id, currentAthlete.Id, cancellationToken);
        return result.ToActionResult(this);
    }

    /// <param name="points">
    /// Upper bound on returned samples. The handler strides the series to fit and
    /// reports the stride it used, so a two-hour ride charts without shipping
    /// seven thousand points to the browser.
    /// </param>
    [HttpGet("{id:guid}/series")]
    [ProducesResponseType<TimeSeriesDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TimeSeriesDto>> Series(
        Guid id,
        [FromQuery] int points = 1000,
        CancellationToken cancellationToken = default)
    {
        var result = await getTimeSeries.ExecuteAsync(id, currentAthlete.Id, points, cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await deleteActivity.ExecuteAsync(id, currentAthlete.Id, cancellationToken);
        return result.ToActionResult(this);
    }

    /// <summary>Activities whose route passes within a radius of a point.</summary>
    [HttpGet("nearby")]
    [ProducesResponseType<IReadOnlyList<NearbyActivityDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<NearbyActivityDto>>> Nearby(
        [FromQuery] double lat,
        [FromQuery] double lon,
        [FromQuery] double radius = 1000,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (lat is < -90 or > 90 || lon is < -180 or > 180)
        {
            return this.ToProblem(Error.Validation("'lat' and 'lon' must be valid WGS-84 coordinates."));
        }

        var result = await findNearby.ExecuteAsync(
            currentAthlete.Id,
            lat,
            lon,
            Math.Clamp(radius, 1, 100_000),
            Math.Clamp(limit, 1, 100),
            cancellationToken);

        return result.ToActionResult(this);
    }
}
