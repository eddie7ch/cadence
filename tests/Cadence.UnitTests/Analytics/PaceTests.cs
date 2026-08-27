using Cadence.Domain.Analytics;
using Shouldly;
using Xunit;

namespace Cadence.UnitTests.Analytics;

public sealed class PaceTests
{
    [Fact]
    public void A_pace_built_from_a_speed_round_trips_back_to_that_speed()
    {
        Pace pace = Pace.FromSpeed(1000.0 / 300.0);

        pace.SecondsPerKilometer.ShouldBe(300, 1e-9);
        pace.MetersPerSecond.ShouldBe(1000.0 / 300.0, 1e-9);
    }

    [Fact]
    public void Five_kilometres_in_twenty_five_minutes_is_five_minutes_per_kilometre()
    {
        Pace pace = Pace.FromDistanceAndDuration(5000, TimeSpan.FromMinutes(25));

        pace.SecondsPerKilometer.ShouldBe(300, 1e-9);
        pace.SecondsPerKilometer.ShouldBe(Pace.FromSpeed(5000.0 / 1500.0).SecondsPerKilometer, 1e-9);
    }

    [Fact]
    public void The_same_pace_expressed_per_mile_and_per_hour_agrees_with_the_metric_figure()
    {
        Pace pace = Pace.FromSecondsPerKilometer(300);

        pace.SecondsPerMile.ShouldBe(482.8032, 1e-6);
        pace.KilometersPerHour.ShouldBe(12.0, 1e-9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_pace_that_is_not_a_positive_finite_number_collapses_to_zero(double seconds)
    {
        Pace.FromSecondsPerKilometer(seconds).ShouldBe(Pace.Zero);
    }

    [Fact]
    public void A_speed_at_or_below_the_noise_floor_is_not_a_pace_at_all()
    {
        Pace.FromSpeed(0).ShouldBe(Pace.Zero);
        Pace.FromSpeed(-3).ShouldBe(Pace.Zero);
        Pace.FromSpeed(1e-9).ShouldBe(Pace.Zero);
    }

    [Fact]
    public void Zero_distance_or_zero_duration_yields_the_zero_pace_rather_than_an_infinity()
    {
        Pace.FromDistanceAndDuration(0, TimeSpan.FromMinutes(25)).ShouldBe(Pace.Zero);
        Pace.FromDistanceAndDuration(5000, TimeSpan.Zero).ShouldBe(Pace.Zero);
        Pace.FromDistanceAndDuration(5000, TimeSpan.FromSeconds(-10)).ShouldBe(Pace.Zero);
    }

    [Fact]
    public void The_zero_pace_reports_zero_speed_instead_of_dividing_by_zero()
    {
        Pace.Zero.MetersPerSecond.ShouldBe(0);
        Pace.Zero.KilometersPerHour.ShouldBe(0);
        Pace.Zero.SecondsPerKilometer.ShouldBe(0);
    }

    [Theory]
    [InlineData(300, "5:00/km")]
    [InlineData(305.6, "5:06/km")]
    [InlineData(59, "0:59/km")]
    [InlineData(3661, "61:01/km")]
    public void A_pace_formats_as_minutes_and_seconds_per_kilometre(double seconds, string expected)
    {
        Pace.FromSecondsPerKilometer(seconds).ToString().ShouldBe(expected);
    }

    [Fact]
    public void An_unknown_pace_formats_as_dashes_rather_than_as_zero()
    {
        Pace.Zero.ToString().ShouldBe("-:--/km");
    }

    [Fact]
    public void Sorting_paces_puts_the_fastest_first()
    {
        Pace slow = Pace.FromSecondsPerKilometer(360);
        Pace quick = Pace.FromSecondsPerKilometer(240);
        Pace middling = Pace.FromSecondsPerKilometer(300);

        List<Pace> sorted = [.. new[] { slow, quick, middling }.Order()];

        sorted.ShouldBe(new[] { quick, middling, slow });
        quick.CompareTo(slow).ShouldBeLessThan(0);
        slow.CompareTo(quick).ShouldBeGreaterThan(0);
        middling.CompareTo(Pace.FromSecondsPerKilometer(300)).ShouldBe(0);
    }

    [Fact]
    public void Two_paces_with_the_same_seconds_per_kilometre_are_equal()
    {
        Pace.FromSecondsPerKilometer(300).ShouldBe(Pace.FromSpeed(1000.0 / 300.0));
    }
}
