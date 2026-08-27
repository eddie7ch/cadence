using Cadence.Application.Abstractions;
using Cadence.Application.Common;
using Cadence.Application.Contracts;
using Cadence.Application.Mapping;
using Cadence.Domain.Activities;
using Cadence.Domain.Analytics;
using Cadence.Domain.Geo;
using Microsoft.Extensions.Logging;
using NetTopologySuite;
using NetTopologySuite.Geometries;

namespace Cadence.Application.Handlers;

/// <summary>
/// Decode, analyse, persist. Re-running this for an activity that already has
/// metrics is safe and expected: a parser fix should be re-appliable to files
/// imported before it, and <see cref="Activity.ApplyMetrics"/> replaces samples
/// and splits rather than adding to them.
/// </summary>
public sealed class ProcessActivityHandler
{
    private const int HeaderBytes = 512;

    private static readonly GeometryFactory Wgs84Factory =
        NtsGeometryServices.Instance.CreateGeometryFactory(GeoMath.Wgs84Srid);

    private readonly IActivityRepository _activities;
    private readonly IActivityFileParserFactory _parserFactory;
    private readonly IActivityFileStore _files;
    private readonly IAnalyticsCache _cache;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly ILogger<ProcessActivityHandler> _logger;

    public ProcessActivityHandler(
        IActivityRepository activities,
        IActivityFileParserFactory parserFactory,
        IActivityFileStore files,
        IAnalyticsCache cache,
        IUnitOfWork unitOfWork,
        IClock clock,
        ILogger<ProcessActivityHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(activities);
        ArgumentNullException.ThrowIfNull(parserFactory);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _activities = activities;
        _parserFactory = parserFactory;
        _files = files;
        _cache = cache;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Everything derived from the file, ready to be handed to the entity.</summary>
    private sealed record Prepared(
        ActivityMetrics Metrics,
        LineString Route,
        LineString SimplifiedRoute,
        List<ActivitySample> Samples,
        List<ActivitySplit> Splits);

    /// <remarks>
    /// Takes only an id: the background worker that calls this has no request, no
    /// athlete, and no business knowing where uploads are kept.
    /// </remarks>
    public async Task<Result<ActivitySummaryDto>> ExecuteAsync(
        Guid activityId,
        CancellationToken cancellationToken = default)
    {
        Activity? activity = await _activities.FindByIdAsync(activityId, cancellationToken);
        if (activity is null)
        {
            return Error.NotFound("Activity not found.");
        }

        await using Stream? content = await _files.OpenAsync(
            activity.AthleteId,
            activity.SourceChecksum,
            activity.SourceFileName,
            cancellationToken);

        if (content is null)
        {
            const string missing = "The stored upload is no longer available.";
            activity.MarkFailed(missing);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Error.NotFound(missing);
        }

        activity.MarkProcessing();

        Prepared? prepared;
        string? failure;

        // Only decoding and analysis are guarded. A failure while saving is a
        // server fault and must not be recorded on the activity as if the
        // athlete had uploaded a bad file.
        try
        {
            (prepared, failure) = await PrepareAsync(activity, content, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Processing activity {ActivityId} failed.", activityId);
            (prepared, failure) = (null, ex.Message);
        }

        if (prepared is null)
        {
            string message = failure ?? "The activity file could not be processed.";
            activity.MarkFailed(message);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Error.Unprocessable(message);
        }

        activity.ApplyMetrics(
            prepared.Metrics,
            prepared.Route,
            prepared.SimplifiedRoute,
            prepared.Samples,
            prepared.Splits,
            _clock.UtcNow);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Every weekly rollup and proximity result for this athlete is now stale.
        await _cache.InvalidateAthleteAsync(activity.AthleteId, cancellationToken);

        return activity.ToSummaryDto();
    }

    private async Task<(Prepared? Prepared, string? Failure)> PrepareAsync(
        Activity activity,
        Stream content,
        CancellationToken cancellationToken)
    {
        // Buffered because the parser needs a rewindable stream and the factory
        // needs the leading bytes; the size bound was applied at import.
        byte[] bytes;
        using (var buffer = new MemoryStream())
        {
            await content.CopyToAsync(buffer, cancellationToken);
            bytes = buffer.ToArray();
        }

        if (bytes.Length == 0)
        {
            return (null, "The stored upload is empty.");
        }

        IActivityFileParser parser = _parserFactory.Resolve(
            activity.SourceFileName,
            bytes.AsSpan(0, Math.Min(HeaderBytes, bytes.Length)));

        ParsedActivity parsed;
        using (var parseBuffer = new MemoryStream(bytes, writable: false))
        {
            parsed = await parser.ParseAsync(parseBuffer, cancellationToken);
        }

        // Heart-rate zones are derived on read rather than stored, so the
        // athlete's configured maximum is not needed here.
        ActivityMetrics metrics = ActivityAnalyzer.Analyze(parsed.Points, AnalysisOptions.Default);

        IReadOnlyList<TrackPoint> cleaned = metrics.CleanedPoints;
        if (cleaned.Count < 2)
        {
            return (null, "The file contains no usable GPS track.");
        }

        LineString route = ToLineString(cleaned);
        LineString simplified = ToLineString(
            RouteSimplifier.Simplify(cleaned, static point => (Lat: point.Latitude, Lon: point.Longitude)));

        return (
            new Prepared(metrics, route, simplified, BuildSamples(activity.Id, metrics), BuildSplits(activity.Id, metrics)),
            null);
    }

    /// <summary>X is longitude and Y is latitude - the order PostGIS and GeoJSON both use.</summary>
    private static LineString ToLineString(IReadOnlyList<TrackPoint> points)
    {
        var coordinates = new Coordinate[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            coordinates[i] = new Coordinate(points[i].Longitude, points[i].Latitude);
        }

        return Wgs84Factory.CreateLineString(coordinates);
    }

    private static List<ActivitySample> BuildSamples(Guid activityId, ActivityMetrics metrics)
    {
        IReadOnlyList<TrackPoint> points = metrics.CleanedPoints;
        IReadOnlyList<double> cumulative = metrics.CumulativeDistanceMeters;

        var samples = new List<ActivitySample>(points.Count);
        DateTimeOffset start = points[0].Timestamp;
        double distance = 0;

        for (int i = 0; i < points.Count; i++)
        {
            TrackPoint point = points[i];

            // The analyser keeps the two lists parallel; carrying the last known
            // value forward keeps distance monotonic if that ever stops holding.
            distance = i < cumulative.Count ? cumulative[i] : distance;

            samples.Add(new ActivitySample(
                activityId,
                i,
                point.Timestamp,
                (point.Timestamp - start).TotalSeconds,
                Wgs84Factory.CreatePoint(new Coordinate(point.Longitude, point.Latitude)),
                distance,
                point.AltitudeMeters,
                point.HeartRateBpm,
                point.CadenceRpm,
                point.PowerWatts,
                point.SpeedMetersPerSecond,
                point.TemperatureCelsius));
        }

        return samples;
    }

    private static List<ActivitySplit> BuildSplits(Guid activityId, ActivityMetrics metrics) =>
        [.. metrics.Splits.Select(split => new ActivitySplit(
            activityId,
            split.Number,
            split.DistanceMeters,
            split.Duration,
            split.Pace.SecondsPerKilometer,
            split.GradeAdjustedPace.SecondsPerKilometer,
            split.ElevationGainMeters,
            split.AverageHeartRateBpm,
            split.IsComplete))];
}
