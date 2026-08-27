using NetTopologySuite.Geometries;

namespace Cadence.Domain.Activities;

/// <summary>
/// One point of the time series. Rows here vastly outnumber every other table -
/// an hour at 1 Hz is 3,600 of them - so the shape is kept narrow and the
/// natural key is (ActivityId, Sequence).
/// </summary>
public sealed class ActivitySample
{
    private ActivitySample()
    {
        Location = null!;
    }

    public ActivitySample(
        Guid activityId,
        int sequence,
        DateTimeOffset timestamp,
        double elapsedSeconds,
        Point location,
        double cumulativeDistanceMeters,
        double? altitudeMeters = null,
        int? heartRateBpm = null,
        int? cadenceRpm = null,
        int? powerWatts = null,
        double? speedMetersPerSecond = null,
        double? temperatureCelsius = null)
    {
        ArgumentNullException.ThrowIfNull(location);

        ActivityId = activityId;
        Sequence = sequence;
        Timestamp = timestamp;
        ElapsedSeconds = elapsedSeconds;
        Location = location;
        CumulativeDistanceMeters = cumulativeDistanceMeters;
        AltitudeMeters = altitudeMeters;
        HeartRateBpm = heartRateBpm;
        CadenceRpm = cadenceRpm;
        PowerWatts = powerWatts;
        SpeedMetersPerSecond = speedMetersPerSecond;
        TemperatureCelsius = temperatureCelsius;
    }

    public Guid ActivityId { get; private set; }

    /// <summary>Zero-based index within the activity; ordering key.</summary>
    public int Sequence { get; private set; }

    public DateTimeOffset Timestamp { get; private set; }

    /// <summary>Seconds since the first sample - denormalised so charts need no date arithmetic.</summary>
    public double ElapsedSeconds { get; private set; }

    public Point Location { get; private set; }

    public double CumulativeDistanceMeters { get; private set; }

    public double? AltitudeMeters { get; private set; }

    public int? HeartRateBpm { get; private set; }

    public int? CadenceRpm { get; private set; }

    public int? PowerWatts { get; private set; }

    public double? SpeedMetersPerSecond { get; private set; }

    public double? TemperatureCelsius { get; private set; }
}
