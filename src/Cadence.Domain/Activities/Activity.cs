using Cadence.Domain.Analytics;
using NetTopologySuite.Geometries;

namespace Cadence.Domain.Activities;

/// <summary>
/// One recorded session. The route is stored as a PostGIS <c>LineString</c> in
/// WGS-84, which is what makes "find activities near here" and "which of my runs
/// overlap this trail" ordinary SQL rather than application-side geometry.
/// </summary>
public sealed class Activity
{
    private readonly List<ActivitySample> _samples = [];
    private readonly List<ActivitySplit> _splits = [];

    private Activity()
    {
        Name = null!;
        SourceFileName = null!;
        SourceChecksum = null!;
    }

    private Activity(
        Guid id,
        Guid athleteId,
        string name,
        Sport sport,
        SourceFormat sourceFormat,
        string sourceFileName,
        string sourceChecksum,
        DateTimeOffset createdAt)
    {
        Id = id;
        AthleteId = athleteId;
        Name = name;
        Sport = sport;
        SourceFormat = sourceFormat;
        SourceFileName = sourceFileName;
        SourceChecksum = sourceChecksum;
        CreatedAt = createdAt;
        Status = ActivityStatus.Pending;
    }

    public Guid Id { get; private set; }

    public Guid AthleteId { get; private set; }

    public string Name { get; private set; }

    public Sport Sport { get; private set; }

    public ActivityStatus Status { get; private set; }

    public SourceFormat SourceFormat { get; private set; }

    public string SourceFileName { get; private set; }

    /// <summary>SHA-256 of the uploaded bytes; unique per athlete, so re-uploading is a no-op.</summary>
    public string SourceChecksum { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public string? Error { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public TimeSpan ElapsedTime { get; private set; }

    public TimeSpan MovingTime { get; private set; }

    public double DistanceMeters { get; private set; }

    public double ElevationGainMeters { get; private set; }

    public double ElevationLossMeters { get; private set; }

    public double AveragePaceSecondsPerKm { get; private set; }

    public double GradeAdjustedPaceSecondsPerKm { get; private set; }

    public int? AverageHeartRateBpm { get; private set; }

    public int? MaxHeartRateBpm { get; private set; }

    public int? AverageCadenceRpm { get; private set; }

    public int? AveragePowerWatts { get; private set; }

    public int SampleCount { get; private set; }

    public int DiscardedSampleCount { get; private set; }

    /// <summary>Full-resolution route, SRID 4326. Null until the file has been processed.</summary>
    public LineString? Route { get; private set; }

    /// <summary>
    /// Douglas-Peucker simplification of <see cref="Route"/>, for map rendering.
    /// Stored rather than computed per request: it is deterministic, it is read
    /// far more often than it is written, and simplifying a 20,000-point track
    /// on every page load is wasted CPU.
    /// </summary>
    public LineString? SimplifiedRoute { get; private set; }

    public IReadOnlyCollection<ActivitySample> Samples => _samples;

    public IReadOnlyCollection<ActivitySplit> Splits => _splits;

    public Pace AveragePace => Pace.FromSecondsPerKilometer(AveragePaceSecondsPerKm);

    public static Activity Import(
        Guid athleteId,
        string name,
        Sport sport,
        SourceFormat sourceFormat,
        string sourceFileName,
        string sourceChecksum,
        DateTimeOffset now) =>
        new(Guid.CreateVersion7(), athleteId, name, sport, sourceFormat, sourceFileName, sourceChecksum, now);

    public void MarkProcessing()
    {
        Status = ActivityStatus.Processing;
        Error = null;
    }

    public void MarkFailed(string error)
    {
        Status = ActivityStatus.Failed;
        Error = error;
        ProcessedAt = null;
    }

    /// <summary>
    /// Applies computed metrics and replaces any samples and splits from an
    /// earlier attempt, so re-processing a file converges rather than
    /// accumulating duplicates.
    /// </summary>
    public void ApplyMetrics(
        ActivityMetrics metrics,
        LineString route,
        LineString simplifiedRoute,
        IEnumerable<ActivitySample> samples,
        IEnumerable<ActivitySplit> splits,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(simplifiedRoute);

        StartedAt = metrics.StartedAt;
        ElapsedTime = metrics.ElapsedTime;
        MovingTime = metrics.MovingTime;
        DistanceMeters = metrics.DistanceMeters;
        ElevationGainMeters = metrics.ElevationGainMeters;
        ElevationLossMeters = metrics.ElevationLossMeters;
        AveragePaceSecondsPerKm = metrics.AveragePace.SecondsPerKilometer;
        GradeAdjustedPaceSecondsPerKm = metrics.GradeAdjustedPace.SecondsPerKilometer;
        AverageHeartRateBpm = metrics.AverageHeartRateBpm;
        MaxHeartRateBpm = metrics.MaxHeartRateBpm;
        AverageCadenceRpm = metrics.AverageCadenceRpm;
        AveragePowerWatts = metrics.AveragePowerWatts;
        DiscardedSampleCount = metrics.DiscardedSampleCount;

        Route = route;
        SimplifiedRoute = simplifiedRoute;

        _samples.Clear();
        _samples.AddRange(samples);
        SampleCount = _samples.Count;

        _splits.Clear();
        _splits.AddRange(splits);

        Status = ActivityStatus.Ready;
        ProcessedAt = now;
        Error = null;
    }

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }
}
