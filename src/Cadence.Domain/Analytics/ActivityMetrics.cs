namespace Cadence.Domain.Analytics;

/// <summary>One completed split (a kilometre by default).</summary>
public sealed record SplitResult(
    int Number,
    double DistanceMeters,
    TimeSpan Duration,
    Pace Pace,
    Pace GradeAdjustedPace,
    double ElevationGainMeters,
    int? AverageHeartRateBpm)
{
    /// <summary>
    /// False for a trailing partial split. The UI must not present the last
    /// 300 m of a run as a suspiciously fast kilometre.
    /// </summary>
    public bool IsComplete { get; init; } = true;
}

/// <summary>
/// Everything derived from a track, computed once at import and cached
/// thereafter. Nothing in here requires the raw samples to recompute.
/// </summary>
public sealed record ActivityMetrics
{
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Wall-clock span from first to last sample.</summary>
    public required TimeSpan ElapsedTime { get; init; }

    /// <summary>Elapsed time minus stoppages and dropouts.</summary>
    public required TimeSpan MovingTime { get; init; }

    public required double DistanceMeters { get; init; }

    public required double ElevationGainMeters { get; init; }

    public required double ElevationLossMeters { get; init; }

    /// <summary>Pace over moving time - what an athlete means by "my pace".</summary>
    public required Pace AveragePace { get; init; }

    public required Pace GradeAdjustedPace { get; init; }

    public int? AverageHeartRateBpm { get; init; }

    public int? MaxHeartRateBpm { get; init; }

    public int? AverageCadenceRpm { get; init; }

    public int? AveragePowerWatts { get; init; }

    public IReadOnlyList<SplitResult> Splits { get; init; } = [];

    /// <summary>Seconds in each heart-rate zone; empty when no zones could be derived.</summary>
    public IReadOnlyDictionary<HeartRateZone, double> ZoneSeconds { get; init; } =
        new Dictionary<HeartRateZone, double>();

    /// <summary>Samples that survived filtering, in order. Used to build the persisted track.</summary>
    public IReadOnlyList<TrackPoint> CleanedPoints { get; init; } = [];

    /// <summary>Cumulative distance at each cleaned point, parallel to <see cref="CleanedPoints"/>.</summary>
    public IReadOnlyList<double> CumulativeDistanceMeters { get; init; } = [];

    /// <summary>Count of samples rejected as implausible; surfaced so import quality is visible.</summary>
    public int DiscardedSampleCount { get; init; }

    public static ActivityMetrics Empty(DateTimeOffset startedAt) => new()
    {
        StartedAt = startedAt,
        ElapsedTime = TimeSpan.Zero,
        MovingTime = TimeSpan.Zero,
        DistanceMeters = 0,
        ElevationGainMeters = 0,
        ElevationLossMeters = 0,
        AveragePace = Pace.Zero,
        GradeAdjustedPace = Pace.Zero,
    };
}
