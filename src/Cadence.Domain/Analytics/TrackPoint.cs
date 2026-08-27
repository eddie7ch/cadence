namespace Cadence.Domain.Analytics;

/// <summary>
/// One decoded sample from a device file, before it becomes a persisted entity.
///
/// The analytics layer works on this rather than on <c>ActivitySample</c> so the
/// algorithms can be unit tested without a database, and so a parser can hand
/// its output straight to the analyser.
/// </summary>
public readonly record struct TrackPoint(
    DateTimeOffset Timestamp,
    double Latitude,
    double Longitude,
    double? AltitudeMeters = null,
    int? HeartRateBpm = null,
    int? CadenceRpm = null,
    int? PowerWatts = null,
    double? SpeedMetersPerSecond = null,
    double? CumulativeDistanceMeters = null,
    double? TemperatureCelsius = null)
{
    /// <summary>
    /// A sample with no fix. Devices emit these while searching for satellites
    /// and they must not be treated as a jump to null island.
    /// </summary>
    public bool HasPosition =>
        Latitude is >= -90 and <= 90
        && Longitude is >= -180 and <= 180
        && !(Math.Abs(Latitude) < 1e-9 && Math.Abs(Longitude) < 1e-9);
}
