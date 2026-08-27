namespace Cadence.Domain.Geo;

/// <summary>
/// Spherical-earth helpers. Everything here operates on WGS-84 degrees and
/// returns metres, which is the only unit that crosses a domain boundary.
/// </summary>
public static class GeoMath
{
    /// <summary>Mean earth radius (IUGG), metres.</summary>
    public const double EarthRadiusMeters = 6_371_008.8;

    public const int Wgs84Srid = 4326;

    /// <summary>
    /// Great-circle distance. Haversine rather than the law of cosines because
    /// consecutive GPS samples are metres apart, and the law of cosines loses
    /// precision badly at that scale.
    /// </summary>
    public static double HaversineDistance(
        double latitude1,
        double longitude1,
        double latitude2,
        double longitude2)
    {
        double phi1 = DegreesToRadians(latitude1);
        double phi2 = DegreesToRadians(latitude2);
        double deltaPhi = phi2 - phi1;
        double deltaLambda = DegreesToRadians(longitude2 - longitude1);

        double a = (Math.Sin(deltaPhi / 2) * Math.Sin(deltaPhi / 2))
            + (Math.Cos(phi1) * Math.Cos(phi2) * Math.Sin(deltaLambda / 2) * Math.Sin(deltaLambda / 2));

        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1.0, Math.Sqrt(a)));
    }

    /// <summary>
    /// Projects a point to local metres relative to an origin, using an
    /// equirectangular approximation. Valid over the span of a single activity
    /// and far cheaper than a full projection, which matters when simplifying a
    /// 20,000-point track.
    /// </summary>
    public static (double X, double Y) ToLocalMeters(
        double latitude,
        double longitude,
        double originLatitude,
        double originLongitude)
    {
        double cosOrigin = Math.Cos(DegreesToRadians(originLatitude));
        double x = DegreesToRadians(longitude - originLongitude) * cosOrigin * EarthRadiusMeters;
        double y = DegreesToRadians(latitude - originLatitude) * EarthRadiusMeters;
        return (x, y);
    }

    public static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    /// <summary>Perpendicular distance from <paramref name="point"/> to the segment a-b, in metres.</summary>
    public static double PerpendicularDistanceMeters(
        (double Lat, double Lon) point,
        (double Lat, double Lon) a,
        (double Lat, double Lon) b)
    {
        (double px, double py) = ToLocalMeters(point.Lat, point.Lon, a.Lat, a.Lon);
        (double bx, double by) = ToLocalMeters(b.Lat, b.Lon, a.Lat, a.Lon);

        double segmentLengthSquared = (bx * bx) + (by * by);
        if (segmentLengthSquared < 1e-12)
        {
            // Degenerate segment: fall back to point-to-point distance.
            return Math.Sqrt((px * px) + (py * py));
        }

        double t = Math.Clamp(((px * bx) + (py * by)) / segmentLengthSquared, 0.0, 1.0);
        double dx = px - (t * bx);
        double dy = py - (t * by);
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
