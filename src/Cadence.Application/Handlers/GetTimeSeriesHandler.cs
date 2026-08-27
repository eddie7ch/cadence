using Cadence.Application.Abstractions;
using Cadence.Application.Common;
using Cadence.Application.Contracts;
using Cadence.Domain.Activities;

namespace Cadence.Application.Handlers;

public sealed class GetTimeSeriesHandler
{
    public const int DefaultMaxPoints = 1_000;

    /// <summary>
    /// A hard ceiling regardless of what the caller asks for. A 20,000-point
    /// series is megabytes of JSON for a chart a few hundred pixels wide, and no
    /// client benefits from more samples than it has pixels.
    /// </summary>
    public const int MaximumMaxPoints = 5_000;

    private readonly IActivityRepository _activities;

    public GetTimeSeriesHandler(IActivityRepository activities)
    {
        ArgumentNullException.ThrowIfNull(activities);
        _activities = activities;
    }

    public async Task<Result<TimeSeriesDto>> ExecuteAsync(
        Guid activityId,
        Guid athleteId,
        int maxPoints = DefaultMaxPoints,
        CancellationToken cancellationToken = default)
    {
        if (maxPoints < 1)
        {
            return Error.Validation("The requested point count must be at least 1.");
        }

        Activity? activity = await _activities.FindByIdAsync(activityId, cancellationToken);
        if (activity is null || activity.AthleteId != athleteId)
        {
            return Error.NotFound("Activity not found.");
        }

        IReadOnlyList<ActivitySample> samples = await _activities.GetSamplesAsync(activityId, cancellationToken);
        if (samples.Count == 0)
        {
            return new TimeSeriesDto([], [], [], [], [], [], [], 1);
        }

        List<ActivitySample> ordered = [.. samples.OrderBy(sample => sample.Sequence)];

        int budget = Math.Min(maxPoints, MaximumMaxPoints);

        // Stride sampling rather than averaging: the chart must show the peaks an
        // athlete cares about, and a mean over a window flattens exactly those.
        int stride = (int)Math.Ceiling(ordered.Count / (double)budget);
        if (stride < 1)
        {
            stride = 1;
        }

        int capacity = ((ordered.Count - 1) / stride) + 1;

        var elapsedSeconds = new List<double>(capacity);
        var distanceMeters = new List<double>(capacity);
        var altitudeMeters = new List<double?>(capacity);
        var heartRateBpm = new List<int?>(capacity);
        var speedMetersPerSecond = new List<double?>(capacity);
        var cadenceRpm = new List<int?>(capacity);
        var powerWatts = new List<int?>(capacity);

        for (int i = 0; i < ordered.Count; i += stride)
        {
            ActivitySample sample = ordered[i];

            elapsedSeconds.Add(sample.ElapsedSeconds);
            distanceMeters.Add(sample.CumulativeDistanceMeters);
            altitudeMeters.Add(sample.AltitudeMeters);
            heartRateBpm.Add(sample.HeartRateBpm);
            speedMetersPerSecond.Add(sample.SpeedMetersPerSecond);
            cadenceRpm.Add(sample.CadenceRpm);
            powerWatts.Add(sample.PowerWatts);
        }

        return new TimeSeriesDto(
            elapsedSeconds,
            distanceMeters,
            altitudeMeters,
            heartRateBpm,
            speedMetersPerSecond,
            cadenceRpm,
            powerWatts,
            stride);
    }
}
