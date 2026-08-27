using System.Collections.ObjectModel;
using System.Globalization;
using Cadence.Application.Abstractions;
using Cadence.Application.Common;
using Cadence.Application.Contracts;
using Cadence.Application.Mapping;
using Cadence.Domain.Activities;
using Cadence.Domain.Analytics;
using Cadence.Domain.Athletes;

namespace Cadence.Application.Handlers;

public sealed class GetActivityDetailHandler
{
    private static readonly TimeSpan ZoneCacheTtl = TimeSpan.FromHours(6);

    private static readonly IReadOnlyDictionary<string, double> NoZones =
        ReadOnlyDictionary<string, double>.Empty;

    private readonly IActivityRepository _activities;
    private readonly IAthleteRepository _athletes;
    private readonly IAnalyticsCache _cache;

    public GetActivityDetailHandler(
        IActivityRepository activities,
        IAthleteRepository athletes,
        IAnalyticsCache cache)
    {
        ArgumentNullException.ThrowIfNull(activities);
        ArgumentNullException.ThrowIfNull(athletes);
        ArgumentNullException.ThrowIfNull(cache);

        _activities = activities;
        _athletes = athletes;
        _cache = cache;
    }

    public async Task<Result<ActivityDetailDto>> ExecuteAsync(
        Guid activityId,
        Guid athleteId,
        CancellationToken cancellationToken = default)
    {
        Activity? activity = await _activities.FindWithSplitsAsync(activityId, cancellationToken);
        if (activity is null || activity.AthleteId != athleteId)
        {
            return Error.NotFound("Activity not found.");
        }

        IReadOnlyDictionary<string, double> zoneSeconds = await ResolveZoneSecondsAsync(activity, cancellationToken);

        return new ActivityDetailDto(
            activity.ToSummaryDto(),
            activity.ToRouteDto(),
            [.. activity.Splits.OrderBy(split => split.Number).Select(split => split.ToDto())],
            zoneSeconds,
            activity.SampleCount,
            activity.DiscardedSampleCount);
    }

    /// <summary>
    /// The zone distribution is not a stored column, so it is derived from the
    /// samples the first time a detail view asks for it and cached until the
    /// athlete's analytics are invalidated. Deriving it per request would mean
    /// loading several thousand rows to render one bar chart.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, double>> ResolveZoneSecondsAsync(
        Activity activity,
        CancellationToken cancellationToken)
    {
        if (activity.Status is not ActivityStatus.Ready || activity.SampleCount == 0)
        {
            return NoZones;
        }

        string key = string.Create(
            CultureInfo.InvariantCulture,
            $"activity:{activity.Id}:heart-rate-zones");

        return await _cache.GetOrCreateAsync(
            activity.AthleteId,
            key,
            ZoneCacheTtl,
            token => ComputeZoneSecondsAsync(activity, token),
            cancellationToken);
    }

    private async Task<Dictionary<string, double>> ComputeZoneSecondsAsync(
        Activity activity,
        CancellationToken cancellationToken)
    {
        Athlete? athlete = await _athletes.FindByIdAsync(activity.AthleteId, cancellationToken);
        IReadOnlyList<ActivitySample> samples = await _activities.GetSamplesAsync(activity.Id, cancellationToken);

        List<TrackPoint> points =
        [
            .. samples
                .OrderBy(sample => sample.Sequence)
                .Select(sample => new TrackPoint(
                    sample.Timestamp,
                    sample.Location.Y,
                    sample.Location.X,
                    sample.AltitudeMeters,
                    sample.HeartRateBpm,
                    sample.CadenceRpm,
                    sample.PowerWatts,
                    sample.SpeedMetersPerSecond,
                    sample.CumulativeDistanceMeters,
                    sample.TemperatureCelsius)),
        ];

        // Zones require a maximum the athlete has actually measured. Falling back
        // to the highest rate in this activity would be circular: the boundaries
        // would be derived from the same session being classified, so every hard
        // effort would report itself as maximal and every easy one as easy. An
        // empty distribution is the honest answer, and the client prompts for the
        // value instead of drawing a chart that means nothing.
        int? maximum = athlete?.MaxHeartRate;

        if (maximum is not > 0)
        {
            return new Dictionary<string, double>();
        }

        HeartRateZones zones = HeartRateZones.ForAthlete(maximum.Value, athlete?.RestingHeartRate);

        return zones
            .Distribution(points, AnalysisOptions.Default.MaximumSampleGap)
            .ToDictionary(entry => entry.Key.ToString(), entry => entry.Value);
    }
}
