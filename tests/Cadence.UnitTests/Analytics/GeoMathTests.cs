using Cadence.Domain.Geo;
using Shouldly;
using Xunit;

namespace Cadence.UnitTests.Analytics;

public sealed class GeoMathTests
{
    private const double CalgaryLatitude = 51.0447;
    private const double CalgaryLongitude = -114.0719;

    /// <summary>Metres per degree of latitude on the sphere <see cref="GeoMath"/> uses.</summary>
    private const double MetersPerDegreeLatitude = Math.PI * GeoMath.EarthRadiusMeters / 180.0;

    [Theory]
    // Independently published great-circle distances, in metres.
    [InlineData(51.0447, -114.0719, 53.5461, -113.4938, 280_900)]   // Calgary - Edmonton
    [InlineData(40.7128, -74.0060, 34.0522, -118.2437, 3_936_000)]  // New York - Los Angeles
    [InlineData(51.5074, -0.1278, 48.8566, 2.3522, 343_500)]        // London - Paris
    public void Haversine_reproduces_a_known_city_pair_distance(
        double latitude1,
        double longitude1,
        double latitude2,
        double longitude2,
        double expectedMeters)
    {
        double distance = GeoMath.HaversineDistance(latitude1, longitude1, latitude2, longitude2);

        // 0.5% covers the spread between published figures, which differ by the
        // earth model each used; it does not cover a wrong formula.
        distance.ShouldBe(expectedMeters, expectedMeters * 0.005);
    }

    [Fact]
    public void Two_identical_points_are_exactly_zero_metres_apart()
    {
        GeoMath.HaversineDistance(CalgaryLatitude, CalgaryLongitude, CalgaryLatitude, CalgaryLongitude)
            .ShouldBe(0);
    }

    [Fact]
    public void Distance_is_the_same_in_either_direction()
    {
        double there = GeoMath.HaversineDistance(51.0447, -114.0719, 53.5461, -113.4938);
        double back = GeoMath.HaversineDistance(53.5461, -113.4938, 51.0447, -114.0719);

        there.ShouldBe(back, 1e-9);
    }

    [Fact]
    public void One_degree_of_latitude_is_one_degree_of_the_great_circle()
    {
        GeoMath.HaversineDistance(51.0, -114.0, 52.0, -114.0)
            .ShouldBe(MetersPerDegreeLatitude, 1e-6);
    }

    [Fact]
    public void Antipodal_points_are_half_a_circumference_apart_rather_than_a_domain_error()
    {
        // The argument to asin reaches its limit of 1 here, and rounding can push
        // it fractionally past; without the clamp that is NaN, not 20,015 km.
        double distance = GeoMath.HaversineDistance(0, 0, 0, 180);

        double.IsNaN(distance).ShouldBeFalse();
        distance.ShouldBe(Math.PI * GeoMath.EarthRadiusMeters, 1e-3);
    }

    [Fact]
    public void Projecting_the_origin_onto_itself_gives_the_local_origin()
    {
        (double x, double y) = GeoMath.ToLocalMeters(
            CalgaryLatitude, CalgaryLongitude, CalgaryLatitude, CalgaryLongitude);

        x.ShouldBe(0);
        y.ShouldBe(0);
    }

    [Fact]
    public void Projecting_a_point_due_north_puts_all_of_the_displacement_on_the_y_axis()
    {
        (double x, double y) = GeoMath.ToLocalMeters(
            North(CalgaryLatitude, 100), CalgaryLongitude, CalgaryLatitude, CalgaryLongitude);

        x.ShouldBe(0, 1e-9);
        y.ShouldBe(100, 1e-6);
    }

    [Fact]
    public void Perpendicular_distance_to_a_degenerate_segment_falls_back_to_point_to_point()
    {
        (double Lat, double Lon) a = (CalgaryLatitude, CalgaryLongitude);
        (double Lat, double Lon) point = (North(CalgaryLatitude, 250), CalgaryLongitude);

        GeoMath.PerpendicularDistanceMeters(point, a, a).ShouldBe(250, 1e-3);
    }

    [Fact]
    public void A_point_on_the_segment_is_zero_metres_from_it()
    {
        (double Lat, double Lon) a = (CalgaryLatitude, CalgaryLongitude);
        (double Lat, double Lon) b = (North(CalgaryLatitude, 1000), CalgaryLongitude);
        (double Lat, double Lon) middle = (North(CalgaryLatitude, 500), CalgaryLongitude);

        GeoMath.PerpendicularDistanceMeters(middle, a, b).ShouldBe(0, 1e-6);
    }

    [Fact]
    public void A_point_beside_the_segment_is_its_sideways_offset_from_it()
    {
        (double Lat, double Lon) a = (CalgaryLatitude, CalgaryLongitude);
        (double Lat, double Lon) b = (North(CalgaryLatitude, 1000), CalgaryLongitude);
        (double Lat, double Lon) offset =
            (North(CalgaryLatitude, 500), East(CalgaryLongitude, CalgaryLatitude, 50));

        GeoMath.PerpendicularDistanceMeters(offset, a, b).ShouldBe(50, 0.1);
    }

    [Fact]
    public void A_point_past_the_end_of_the_segment_measures_to_the_endpoint_not_to_the_infinite_line()
    {
        (double Lat, double Lon) a = (CalgaryLatitude, CalgaryLongitude);
        (double Lat, double Lon) b = (North(CalgaryLatitude, 1000), CalgaryLongitude);
        (double Lat, double Lon) beyond = (North(CalgaryLatitude, 1222), CalgaryLongitude);

        // On the infinite line this would be zero; clamped to the segment it is
        // the 222 m overshoot past b.
        GeoMath.PerpendicularDistanceMeters(beyond, a, b).ShouldBe(222, 1e-3);
    }

    [Fact]
    public void Degrees_convert_to_radians()
    {
        GeoMath.DegreesToRadians(180).ShouldBe(Math.PI, 1e-12);
        GeoMath.DegreesToRadians(-90).ShouldBe(-Math.PI / 2, 1e-12);
        GeoMath.DegreesToRadians(0).ShouldBe(0);
    }

    private static double North(double latitude, double meters) =>
        latitude + (meters / MetersPerDegreeLatitude);

    private static double East(double longitude, double latitude, double meters) =>
        longitude + (meters / (MetersPerDegreeLatitude * Math.Cos(GeoMath.DegreesToRadians(latitude))));
}
