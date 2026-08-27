using Cadence.Application.Abstractions;
using Cadence.Application.Contracts;
using Cadence.Domain.Activities;
using Cadence.Domain.Athletes;
using Cadence.Domain.Coaching;
using NetTopologySuite.Geometries;

namespace Cadence.Application.Mapping;

/// <summary>
/// Entity to wire-contract projections. Kept in one place so the coordinate
/// order, the enum-to-string spelling, and the unit of every field are decided
/// once rather than re-derived at each call site.
/// </summary>
public static class DtoMappings
{
    public static AthleteDto ToDto(this Athlete athlete)
    {
        ArgumentNullException.ThrowIfNull(athlete);

        return new AthleteDto(
            athlete.Id,
            athlete.Email,
            athlete.DisplayName,
            athlete.MaxHeartRate,
            athlete.RestingHeartRate,
            athlete.CreatedAt);
    }

    public static ActivitySummaryDto ToSummaryDto(this Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        return new ActivitySummaryDto(
            activity.Id,
            activity.Name,
            activity.Sport.ToString(),
            activity.Status.ToString(),
            activity.StartedAt,
            activity.DistanceMeters,
            activity.MovingTime.TotalSeconds,
            activity.ElapsedTime.TotalSeconds,
            activity.ElevationGainMeters,
            activity.AveragePaceSecondsPerKm,
            activity.GradeAdjustedPaceSecondsPerKm,
            activity.AverageHeartRateBpm,
            activity.Error);
    }

    public static NearbyActivityDto ToNearbyDto(this Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        return new NearbyActivityDto(
            activity.Id,
            activity.Name,
            activity.Sport.ToString(),
            activity.StartedAt,
            activity.DistanceMeters,
            activity.AveragePaceSecondsPerKm);
    }

    public static SplitDto ToDto(this ActivitySplit split)
    {
        ArgumentNullException.ThrowIfNull(split);

        return new SplitDto(
            split.Number,
            split.DistanceMeters,
            split.Duration.TotalSeconds,
            split.PaceSecondsPerKm,
            split.GradeAdjustedPaceSecondsPerKm,
            split.ElevationGainMeters,
            split.AverageHeartRateBpm,
            split.IsComplete);
    }

    /// <summary>
    /// The rendered polyline is the simplified route: it is what a map needs, and
    /// the full track is an order of magnitude larger for a shape that is
    /// pixel-identical. The counts let a client see how much was dropped, and the
    /// bounding box is taken from the full route so a fit-to-bounds never clips a
    /// vertex simplification removed.
    /// </summary>
    public static RouteDto? ToRouteDto(this Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        if (activity.Route is not { IsEmpty: false } route)
        {
            return null;
        }

        LineString rendered = activity.SimplifiedRoute is { IsEmpty: false } simplified ? simplified : route;

        Coordinate[] source = rendered.Coordinates;
        var coordinates = new double[source.Length][];
        for (int i = 0; i < source.Length; i++)
        {
            coordinates[i] = [source[i].X, source[i].Y];
        }

        Envelope envelope = route.EnvelopeInternal;

        return new RouteDto(
            coordinates,
            [envelope.MinX, envelope.MinY, envelope.MaxX, envelope.MaxY],
            route.NumPoints,
            rendered.NumPoints);
    }

    public static WeeklyTotalsDto ToDto(this WeeklyTotals totals)
    {
        ArgumentNullException.ThrowIfNull(totals);

        return new WeeklyTotalsDto(
            totals.WeekStart,
            totals.ActivityCount,
            totals.DistanceMeters,
            totals.ElevationGainMeters,
            totals.MovingSeconds,
            totals.AverageHeartRateBpm);
    }

    public static CoachingFindingDto ToDto(this CoachingFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);

        return new CoachingFindingDto(finding.Title, finding.Detail, finding.Metric, finding.Severity);
    }

    public static CoachingReportDto ToDto(this CoachingReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        return new CoachingReportDto(
            report.Id,
            report.PeriodStart,
            report.PeriodEnd,
            report.Summary,
            report.Verdict.ToString(),
            [.. report.Findings.Select(finding => finding.ToDto())],
            report.ActivityCount,
            report.ModelId,
            report.GeneratedAt);
    }

    public static PagedDto<TDto> ToDto<TEntity, TDto>(this PagedResult<TEntity> page, Func<TEntity, TDto> projection)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(projection);

        return new PagedDto<TDto>(
            [.. page.Items.Select(projection)],
            page.Page,
            page.PageSize,
            page.TotalCount,
            page.TotalPages);
    }
}
