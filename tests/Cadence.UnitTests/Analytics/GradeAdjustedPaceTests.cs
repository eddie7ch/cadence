using Cadence.Domain.Analytics;
using Shouldly;
using Xunit;

namespace Cadence.UnitTests.Analytics;

public sealed class GradeAdjustedPaceTests
{
    [Fact]
    public void Running_on_the_flat_needs_no_adjustment_at_all()
    {
        GradeAdjustedPace.CostOfRunning(0).ShouldBe(GradeAdjustedPace.FlatCostJoulesPerKgPerMeter);
        GradeAdjustedPace.AdjustmentFactor(0).ShouldBe(1.0);
    }

    [Fact]
    public void Running_uphill_costs_more_than_running_on_the_flat()
    {
        GradeAdjustedPace.AdjustmentFactor(0.05).ShouldBeGreaterThan(1.0);
        GradeAdjustedPace.AdjustmentFactor(0.10).ShouldBeGreaterThan(1.0);
        GradeAdjustedPace.AdjustmentFactor(0.20).ShouldBeGreaterThan(1.0);
    }

    [Fact]
    public void The_uphill_side_of_the_curve_gets_steadily_more_expensive()
    {
        for (int percent = 0; percent < 45; percent++)
        {
            double gentler = GradeAdjustedPace.AdjustmentFactor(percent / 100.0);
            double steeper = GradeAdjustedPace.AdjustmentFactor((percent + 1) / 100.0);

            steeper.ShouldBeGreaterThan(gentler);
        }
    }

    [Fact]
    public void A_gentle_descent_is_cheaper_than_the_flat_but_a_steep_one_is_dearer_again()
    {
        double flat = GradeAdjustedPace.AdjustmentFactor(0);
        double barelyDownhill = GradeAdjustedPace.AdjustmentFactor(-0.02);
        double gentleDescent = GradeAdjustedPace.AdjustmentFactor(-0.10);
        double steepDescent = GradeAdjustedPace.AdjustmentFactor(-0.35);
        double plummeting = GradeAdjustedPace.AdjustmentFactor(-0.45);

        // Down, then back up: the shape a "downhill is always easier" adjustment
        // gets wrong, and the reason braking cost has to be modelled at all.
        gentleDescent.ShouldBeLessThan(barelyDownhill);
        barelyDownhill.ShouldBeLessThan(flat);
        steepDescent.ShouldBeGreaterThan(gentleDescent);
        plummeting.ShouldBeGreaterThan(steepDescent);
        plummeting.ShouldBeGreaterThan(flat);
    }

    [Fact]
    public void The_cheapest_gradient_to_run_is_a_shallow_descent_and_not_the_flat()
    {
        double cheapestGradient = 0;
        double cheapestFactor = double.MaxValue;

        for (int thousandths = -450; thousandths <= 450; thousandths++)
        {
            double gradient = thousandths / 1000.0;
            double factor = GradeAdjustedPace.AdjustmentFactor(gradient);

            if (factor < cheapestFactor)
            {
                cheapestFactor = factor;
                cheapestGradient = gradient;
            }
        }

        cheapestGradient.ShouldBeLessThan(0);
        cheapestFactor.ShouldBeLessThan(1.0);

        // Minetti's polynomial bottoms out at roughly -18%; the range below is
        // wide enough not to pin the exact coefficients, narrow enough to fail if
        // the minimum drifts onto the flat or off the fitted range.
        cheapestGradient.ShouldBeInRange(-0.30, -0.05);
    }

    [Theory]
    [InlineData(0.60)]
    [InlineData(1.50)]
    [InlineData(100.0)]
    public void Gradients_past_the_fitted_range_clamp_uphill_rather_than_diverge(double gradient)
    {
        GradeAdjustedPace.CostOfRunning(gradient)
            .ShouldBe(GradeAdjustedPace.CostOfRunning(GradeAdjustedPace.MaxAbsoluteGradient));

        // The quintic term would run away without the clamp; a cliff is not a wall.
        GradeAdjustedPace.AdjustmentFactor(gradient).ShouldBeLessThan(6.0);
    }

    [Theory]
    [InlineData(-0.60)]
    [InlineData(-3.00)]
    [InlineData(-100.0)]
    public void Gradients_past_the_fitted_range_clamp_downhill_rather_than_diverge(double gradient)
    {
        GradeAdjustedPace.CostOfRunning(gradient)
            .ShouldBe(GradeAdjustedPace.CostOfRunning(-GradeAdjustedPace.MaxAbsoluteGradient));

        GradeAdjustedPace.AdjustmentFactor(gradient).ShouldBeInRange(0.1, 2.0);
    }

    [Fact]
    public void Adjusting_a_pace_on_the_flat_leaves_it_where_it_was()
    {
        Pace actual = Pace.FromSecondsPerKilometer(300);

        GradeAdjustedPace.Adjust(actual, 0).SecondsPerKilometer.ShouldBe(300, 1e-9);
    }

    [Fact]
    public void A_pace_run_uphill_is_worth_a_faster_pace_on_the_flat()
    {
        Pace actual = Pace.FromSecondsPerKilometer(300);

        Pace adjusted = GradeAdjustedPace.Adjust(actual, 0.10);

        // 5:00/km up a 10% grade is the effort of roughly 3:01/km on the flat.
        adjusted.SecondsPerKilometer.ShouldBe(180.96, 0.05);
        adjusted.SecondsPerKilometer.ShouldBeLessThan(actual.SecondsPerKilometer);
    }

    [Fact]
    public void A_pace_run_down_a_gentle_slope_is_worth_a_slower_pace_on_the_flat()
    {
        Pace actual = Pace.FromSecondsPerKilometer(300);

        GradeAdjustedPace.Adjust(actual, -0.10).SecondsPerKilometer
            .ShouldBeGreaterThan(actual.SecondsPerKilometer);
    }

    [Fact]
    public void There_is_nothing_to_adjust_when_there_is_no_pace()
    {
        GradeAdjustedPace.Adjust(Pace.Zero, 0.10).ShouldBe(Pace.Zero);
    }

    [Fact]
    public void A_flat_series_of_segments_adjusts_to_the_pace_actually_run()
    {
        Pace pace = GradeAdjustedPace.OverSegments(
        [
            (1000.0, 0.0, 300.0),
            (1000.0, 0.0, 320.0),
        ]);

        pace.SecondsPerKilometer.ShouldBe(310, 1e-9);
    }

    [Fact]
    public void Segments_are_weighted_by_their_length_and_not_by_how_long_they_took()
    {
        // 1 km flat in 5:00, then 100 m up a 20% wall that takes 100 s.
        (double DistanceMeters, double RiseMeters, double Seconds)[] segments =
        [
            (1000.0, 0.0, 300.0),
            (100.0, 20.0, 100.0),
        ];

        Pace pace = GradeAdjustedPace.OverSegments(segments);

        // Distance-weighted: 1000 m + 100 m x 2.502 = 1250 m equivalent in 400 s.
        pace.SecondsPerKilometer.ShouldBe(319.95, 0.1);

        // Weighting by time instead would let the slow climb dominate and hand
        // back roughly 264 s/km - faster than the flat kilometre actually run,
        // which is the tell that the weighting is wrong.
        pace.SecondsPerKilometer.ShouldBeGreaterThan(300);
    }

    [Fact]
    public void Segments_with_no_length_or_no_duration_are_ignored_rather_than_dividing_by_zero()
    {
        Pace pace = GradeAdjustedPace.OverSegments(
        [
            (0.0, 0.0, 10.0),
            (1000.0, 0.0, 300.0),
            (-5.0, 1.0, 3.0),
            (500.0, 0.0, 0.0),
        ]);

        pace.SecondsPerKilometer.ShouldBe(300, 1e-9);
    }

    [Fact]
    public void No_segments_means_no_pace()
    {
        GradeAdjustedPace.OverSegments([]).ShouldBe(Pace.Zero);
    }

    [Fact]
    public void Adjusting_over_a_missing_series_is_a_programming_error()
    {
        Should.Throw<ArgumentNullException>(() => GradeAdjustedPace.OverSegments(null!));
    }
}
