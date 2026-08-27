using Cadence.Domain.Analytics;
using Cadence.Domain.Geo;

namespace Cadence.UnitTests.TestData;

/// <summary>
/// Builds a synthetic track by walking due north along a meridian.
///
/// Along a meridian the Haversine distance between two samples is exactly the
/// metres travelled, so a test can say "run at 3 m/s for 200 s" and then assert
/// on 600 m without restating the distance formula it is trying to verify.
/// </summary>
public sealed class TrackBuilder
{
    /// <summary>Metres per degree of latitude on the same sphere <see cref="GeoMath"/> uses.</summary>
    private const double MetersPerDegreeLatitude = Math.PI * GeoMath.EarthRadiusMeters / 180.0;

    private static readonly TimeSpan DefaultSampleInterval = TimeSpan.FromSeconds(1);

    private readonly List<TrackPoint> _points = [];

    private DateTimeOffset _time;
    private double _latitude;
    private double _longitude;
    private double _altitudeMeters;
    private int? _heartRateBpm;
    private int? _cadenceRpm;
    private double? _deviceDistanceScale;

    private TrackBuilder(DateTimeOffset start, double latitude, double longitude, double altitudeMeters)
    {
        _time = start;
        _latitude = latitude;
        _longitude = longitude;
        _altitudeMeters = altitudeMeters;
        Append();
    }

    /// <summary>Seeds a track with a single sample at <paramref name="start"/>.</summary>
    public static TrackBuilder StartingAt(
        DateTimeOffset start,
        double latitude = 51.0447,
        double longitude = -114.0719,
        double altitudeMeters = 1045.0) => new(start, latitude, longitude, altitudeMeters);

    /// <summary>Appends samples travelling north at a constant speed on a constant gradient.</summary>
    public TrackBuilder Move(
        double speedMetersPerSecond,
        TimeSpan duration,
        double gradient = 0.0,
        TimeSpan? sampleInterval = null)
    {
        TimeSpan interval = sampleInterval ?? DefaultSampleInterval;
        int samples = (int)Math.Round(duration.TotalSeconds / interval.TotalSeconds);
        double step = speedMetersPerSecond * interval.TotalSeconds;

        for (int i = 0; i < samples; i++)
        {
            _time += interval;
            _latitude += step / MetersPerDegreeLatitude;
            _altitudeMeters += step * gradient;
            Append();
        }

        return this;
    }

    /// <summary>Standing still while the device keeps logging - a traffic light, not a dropout.</summary>
    public TrackBuilder Pause(TimeSpan duration, TimeSpan? sampleInterval = null) =>
        Move(0, duration, 0, sampleInterval);

    /// <summary>
    /// A single sample displaced from the previous one with nothing in between:
    /// a device dropout when <paramref name="after"/> is long, a fast leg when it
    /// is short.
    /// </summary>
    public TrackBuilder Jump(double northMeters, TimeSpan after, double riseMeters = 0)
    {
        _time += after;
        _latitude += northMeters / MetersPerDegreeLatitude;
        _altitudeMeters += riseMeters;
        Append();
        return this;
    }

    /// <summary>
    /// One bad fix. The track carries on from where it really was, which is what
    /// a receiver does after it reacquires - the glitch is a single outlier, not
    /// a permanent shift of the whole route.
    /// </summary>
    public TrackBuilder Glitch(double offsetMeters, TimeSpan? after = null)
    {
        double trueLatitude = _latitude;
        _time += after ?? DefaultSampleInterval;
        _latitude += offsetMeters / MetersPerDegreeLatitude;
        Append();
        _latitude = trueLatitude;
        return this;
    }

    /// <summary>Applies from the current sample onward.</summary>
    public TrackBuilder WithHeartRate(int beatsPerMinute)
    {
        _heartRateBpm = beatsPerMinute;
        _points[^1] = _points[^1] with { HeartRateBpm = beatsPerMinute };
        return this;
    }

    /// <summary>Applies from the current sample onward.</summary>
    public TrackBuilder WithCadence(int revolutionsPerMinute)
    {
        _cadenceRpm = revolutionsPerMinute;
        _points[^1] = _points[^1] with { CadenceRpm = revolutionsPerMinute };
        return this;
    }

    /// <summary>
    /// Stamps every sample with a device odometer reading. A scale other than 1
    /// makes the device disagree with the GPS deliberately, so a test can tell
    /// which of the two a calculation actually used.
    /// </summary>
    public TrackBuilder WithDeviceDistance(double scale = 1.0)
    {
        _deviceDistanceScale = scale;
        return this;
    }

    public IReadOnlyList<TrackPoint> Build()
    {
        if (_deviceDistanceScale is not { } scale)
        {
            return [.. _points];
        }

        var stamped = new TrackPoint[_points.Count];
        stamped[0] = _points[0] with { CumulativeDistanceMeters = 0 };

        double traveled = 0;
        for (int i = 1; i < _points.Count; i++)
        {
            traveled += GeoMath.HaversineDistance(
                _points[i - 1].Latitude,
                _points[i - 1].Longitude,
                _points[i].Latitude,
                _points[i].Longitude);
            stamped[i] = _points[i] with { CumulativeDistanceMeters = scale * traveled };
        }

        return stamped;
    }

    private void Append() => _points.Add(new TrackPoint(
        _time,
        _latitude,
        _longitude,
        _altitudeMeters,
        _heartRateBpm,
        _cadenceRpm));
}
