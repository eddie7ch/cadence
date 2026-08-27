namespace Cadence.Domain.Analytics;

/// <summary>
/// Tuning for <see cref="ActivityAnalyzer"/>. Every default here is a judgement
/// call about noisy consumer hardware rather than a constant of nature, so they
/// are all overridable and all documented.
/// </summary>
public sealed record AnalysisOptions
{
    public static AnalysisOptions Default { get; } = new();

    /// <summary>
    /// Below this speed the athlete is considered stopped. 0.5 m/s is roughly
    /// 33 min/km - slower than any deliberate movement, but fast enough to
    /// discard the metre-scale wander a stationary GPS produces at a traffic
    /// light.
    /// </summary>
    public double MovingSpeedThresholdMetersPerSecond { get; init; } = 0.5;

    /// <summary>
    /// An interval longer than this is treated as a pause or a device dropout,
    /// not as elapsed activity. Without it, a watch left recording overnight
    /// reports an eight-hour run.
    /// </summary>
    public TimeSpan MaximumSampleGap { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Split length; 1 km by default, 1609.344 m for imperial mile splits.</summary>
    public double SplitDistanceMeters { get; init; } = Pace.MetersPerKilometer;

    public double ElevationThresholdMeters { get; init; } = ElevationProfile.DefaultThresholdMeters;

    public int ElevationSmoothingWindow { get; init; } = ElevationProfile.DefaultSmoothingWindow;

    /// <summary>
    /// A single GPS sample can jump hundreds of metres under a bridge or between
    /// buildings. Any segment implying a speed above this is discarded rather
    /// than allowed to inflate the distance.
    /// </summary>
    public double MaximumPlausibleSpeedMetersPerSecond { get; init; } = 30.0;

    public int? MaxHeartRate { get; init; }

    public int? RestingHeartRate { get; init; }
}
