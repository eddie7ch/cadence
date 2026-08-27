using Cadence.Domain.Analytics;
using Cadence.Domain.Geo;
using Shouldly;
using Xunit;

namespace Cadence.UnitTests.Analytics;

public sealed class RouteSimplifierTests
{
    private const double OriginLatitude = 51.0447;
    private const double OriginLongitude = -114.0719;
    private const double MetersPerDegreeLatitude = Math.PI * GeoMath.EarthRadiusMeters / 180.0;

    [Fact]
    public void A_straight_line_collapses_to_its_two_endpoints()
    {
        // Eleven points on one bearing carry no shape the other nine could add.
        List<(double Lat, double Lon)> straight = [.. Enumerable.Range(0, 11).Select(i => Point(i * 10.0, 0))];

        IReadOnlyList<(double Lat, double Lon)> simplified =
            RouteSimplifier.Simplify(straight, p => p, toleranceMeters: 5);

        simplified.Count.ShouldBe(2);
        simplified[0].ShouldBe(straight[0]);
        simplified[1].ShouldBe(straight[^1]);
    }

    [Fact]
    public void A_corner_further_from_the_line_than_the_tolerance_survives()
    {
        List<(double Lat, double Lon)> dogLeg = [Point(0, 0), Point(500, 50), Point(1000, 0)];

        RouteSimplifier.Simplify(dogLeg, p => p, toleranceMeters: 5).Count.ShouldBe(3);
        RouteSimplifier.Simplify(dogLeg, p => p, toleranceMeters: 100).Count.ShouldBe(2);
    }

    [Fact]
    public void The_first_and_last_points_are_kept_no_matter_how_coarse_the_tolerance()
    {
        List<(double Lat, double Lon)> zigzag = Zigzag(200);

        IReadOnlyList<(double Lat, double Lon)> simplified =
            RouteSimplifier.Simplify(zigzag, p => p, toleranceMeters: 10_000);

        simplified.Count.ShouldBe(2);
        simplified[0].ShouldBe(zigzag[0]);
        simplified[^1].ShouldBe(zigzag[^1]);
    }

    [Fact]
    public void A_coarser_tolerance_never_returns_more_points_than_a_finer_one()
    {
        List<(double Lat, double Lon)> zigzag = Zigzag(400);
        double[] tolerances = [0.5, 1, 2, 5, 20, 100, 1000];

        int previousCount = int.MaxValue;
        foreach (double tolerance in tolerances)
        {
            int count = RouteSimplifier.Simplify(zigzag, p => p, tolerance).Count;

            count.ShouldBeLessThanOrEqualTo(previousCount);
            count.ShouldBeGreaterThanOrEqualTo(2);
            previousCount = count;
        }
    }

    [Fact]
    public void Simplifying_keeps_the_surviving_points_in_their_original_order()
    {
        List<(double Lat, double Lon)> zigzag = Zigzag(200);

        IReadOnlyList<(double Lat, double Lon)> simplified =
            RouteSimplifier.Simplify(zigzag, p => p, toleranceMeters: 2);

        // The track only ever runs north, so a preserved order is a strictly
        // increasing latitude.
        for (int i = 1; i < simplified.Count; i++)
        {
            simplified[i].Lat.ShouldBeGreaterThan(simplified[i - 1].Lat);
        }

        simplified.ShouldBeSubsetOf(zigzag);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void A_track_too_short_to_have_an_interior_is_handed_straight_back(int count)
    {
        List<(double Lat, double Lon)> points = [.. Enumerable.Range(0, count).Select(i => Point(i * 10.0, 0))];

        RouteSimplifier.Simplify(points, p => p, toleranceMeters: 5).ShouldBeSameAs(points);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_tolerance_of_zero_or_less_asks_for_no_simplification_and_gets_none(double tolerance)
    {
        List<(double Lat, double Lon)> zigzag = Zigzag(50);

        RouteSimplifier.Simplify(zigzag, p => p, tolerance).ShouldBeSameAs(zigzag);
    }

    [Fact]
    public void Simplification_works_on_whatever_type_the_caller_holds_its_route_in()
    {
        var start = new DateTimeOffset(2026, 4, 12, 13, 0, 0, TimeSpan.Zero);
        List<TrackPoint> track =
        [
            .. Enumerable.Range(0, 11).Select(i =>
            {
                (double lat, double lon) = Point(i * 10.0, 0);
                return new TrackPoint(start.AddSeconds(i), lat, lon);
            }),
        ];

        IReadOnlyList<TrackPoint> simplified =
            RouteSimplifier.Simplify(track, p => (p.Latitude, p.Longitude), toleranceMeters: 5);

        simplified.Count.ShouldBe(2);
        simplified[0].ShouldBe(track[0]);
        simplified[1].ShouldBe(track[^1]);
    }

    [Fact]
    public void Simplifying_nothing_at_all_is_a_programming_error()
    {
        List<(double Lat, double Lon)> points = Zigzag(10);

        Should.Throw<ArgumentNullException>(
            () => RouteSimplifier.Simplify<(double Lat, double Lon)>(null!, p => p));
        Should.Throw<ArgumentNullException>(() => RouteSimplifier.Simplify(points, null!));
    }

    /// <summary>A northbound track that wanders a few metres either side of its centre line.</summary>
    private static List<(double Lat, double Lon)> Zigzag(int count) =>
        [.. Enumerable.Range(0, count).Select(i => Point(i * 10.0, ((i % 7) - 3) * 3.0))];

    private static (double Lat, double Lon) Point(double northMeters, double eastMeters) =>
        (OriginLatitude + (northMeters / MetersPerDegreeLatitude),
         OriginLongitude + (eastMeters / (MetersPerDegreeLatitude * Math.Cos(GeoMath.DegreesToRadians(OriginLatitude)))));
}
