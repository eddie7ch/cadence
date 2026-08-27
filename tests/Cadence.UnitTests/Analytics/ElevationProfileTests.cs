using Cadence.Domain.Analytics;
using Shouldly;
using Xunit;

namespace Cadence.UnitTests.Analytics;

public sealed class ElevationProfileTests
{
    [Fact]
    public void A_flat_track_with_a_metre_of_sensor_noise_reports_no_climb_at_all()
    {
        // One hour at 1 Hz on ground that never changes height, with the jitter a
        // consumer barometer or GPS altitude actually produces. This is the case
        // the whole filter exists for: summing raw deltas here invents a
        // kilometre of climb that the athlete never ran.
        double[] noisy = [.. SensorNoise(3600, amplitudeMeters: 1.0).Select(n => 100.0 + n)];

        ElevationProfile.Result result = ElevationProfile.Compute([.. noisy.Select(a => (double?)a)]);

        NaiveGain(noisy).ShouldBeGreaterThan(500);
        result.GainMeters.ShouldBe(0);
        result.LossMeters.ShouldBe(0);
    }

    [Fact]
    public void A_genuine_hundred_metre_climb_is_reported_as_a_hundred_metres()
    {
        // 1,000 samples rising 0.1 m each: a real, steady ascent well clear of
        // the hysteresis threshold.
        double?[] climb = [.. Enumerable.Range(0, 1001).Select(i => (double?)(1000.0 + (0.1 * i)))];

        ElevationProfile.Result result = ElevationProfile.Compute(climb);

        // Slightly under 100: smoothing trims the two ends, and the ratchet only
        // books whole threshold-sized steps.
        result.GainMeters.ShouldBe(100, 2);
        result.LossMeters.ShouldBe(0);
    }

    [Fact]
    public void A_genuine_hundred_metre_descent_is_reported_as_loss_and_not_as_gain()
    {
        double?[] descent = [.. Enumerable.Range(0, 1001).Select(i => (double?)(1100.0 - (0.1 * i)))];

        ElevationProfile.Result result = ElevationProfile.Compute(descent);

        result.LossMeters.ShouldBe(100, 2);
        result.GainMeters.ShouldBe(0);
    }

    [Fact]
    public void A_climb_that_is_walked_back_down_reports_both_the_gain_and_the_loss()
    {
        double?[] outAndBack =
        [
            .. Enumerable.Range(0, 501).Select(i => (double?)(1000.0 + (0.2 * i))),
            .. Enumerable.Range(1, 500).Select(i => (double?)(1100.0 - (0.2 * i))),
        ];

        ElevationProfile.Result result = ElevationProfile.Compute(outAndBack);

        result.GainMeters.ShouldBe(100, 3);
        result.LossMeters.ShouldBe(100, 3);
    }

    [Fact]
    public void A_bump_smaller_than_the_threshold_is_never_booked_as_climb()
    {
        // A 2 m rise under a 3 m ratchet: real, but below the resolution the
        // filter is willing to claim.
        double?[] altitudes = [.. Enumerable.Repeat((double?)100.0, 50), .. Enumerable.Repeat((double?)102.0, 50)];

        ElevationProfile.Result result = ElevationProfile.Compute(altitudes);

        result.GainMeters.ShouldBe(0);
        result.LossMeters.ShouldBe(0);
    }

    [Fact]
    public void Dropped_altitudes_are_filled_from_the_last_known_reading_rather_than_treated_as_sea_level()
    {
        double?[] withGaps = [null, null, 10.0, null, null, 20.0];

        // Window of one so the assertion sees the filled series, not the smoothed one.
        ElevationProfile.Result result = ElevationProfile.Compute(withGaps, smoothingWindow: 1);

        result.Smoothed.ShouldBe(new double[] { 10.0, 10.0, 10.0, 10.0, 10.0, 20.0 });
        result.GainMeters.ShouldBe(10);
        result.LossMeters.ShouldBe(0);
    }

    [Fact]
    public void A_leading_gap_is_backfilled_from_the_first_real_reading()
    {
        ElevationProfile.Result result = ElevationProfile.Compute([null, null, 42.0], smoothingWindow: 1);

        result.Smoothed.ShouldBe(new double[] { 42.0, 42.0, 42.0 });
    }

    [Fact]
    public void The_smoothed_series_is_the_same_length_as_the_input_so_it_stays_aligned_with_the_samples()
    {
        // Callers index the smoothed array alongside their own sample list; a
        // shorter result would silently pair every altitude with the wrong point.
        double?[] altitudes = [.. Enumerable.Range(0, 250).Select(i => i % 7 == 0 ? (double?)null : 500.0 + i)];

        ElevationProfile.Result result = ElevationProfile.Compute(altitudes);

        result.Smoothed.Length.ShouldBe(altitudes.Length);
    }

    [Fact]
    public void A_series_with_no_usable_altitude_at_all_produces_nothing_rather_than_zeroes()
    {
        ElevationProfile.Result allNull = ElevationProfile.Compute([null, null, null]);

        allNull.Smoothed.ShouldBeEmpty();
        allNull.GainMeters.ShouldBe(0);
        allNull.LossMeters.ShouldBe(0);

        ElevationProfile.Result empty = ElevationProfile.Compute([]);

        empty.Smoothed.ShouldBeEmpty();
        empty.GainMeters.ShouldBe(0);
    }

    [Fact]
    public void Infinite_and_not_a_number_altitudes_are_treated_as_missing()
    {
        ElevationProfile.Result result = ElevationProfile.Compute(
            [100.0, double.NaN, double.PositiveInfinity, 100.0],
            smoothingWindow: 1);

        result.Smoothed.ShouldBe(new double[] { 100.0, 100.0, 100.0, 100.0 });
        result.GainMeters.ShouldBe(0);
    }

    [Fact]
    public void Computing_a_profile_from_nothing_is_a_programming_error()
    {
        Should.Throw<ArgumentNullException>(() => ElevationProfile.Compute(null!));
    }

    [Fact]
    public void A_moving_average_leaves_a_constant_series_untouched()
    {
        double[] constant = [7, 7, 7, 7, 7, 7, 7];

        ElevationProfile.MovingAverage(constant, 5).ShouldBe(constant);
    }

    [Fact]
    public void A_moving_average_clamps_its_window_at_both_ends_instead_of_padding_them()
    {
        // Padding with zeroes would drag the first and last values toward zero and
        // manufacture a cliff at each end of every track.
        double[] ramp = [0, 1, 2, 3, 4];

        double[] smoothed = ElevationProfile.MovingAverage(ramp, 3);

        smoothed.ShouldBe(new double[] { 0.5, 1, 2, 3, 3.5 });
    }

    [Fact]
    public void A_moving_average_with_no_window_to_average_over_returns_the_input()
    {
        double[] values = [1, 5, 2];

        ElevationProfile.MovingAverage(values, 1).ShouldBeSameAs(values);
        ElevationProfile.MovingAverage(values, 0).ShouldBeSameAs(values);
    }

    private static double NaiveGain(IReadOnlyList<double> altitudes)
    {
        double gain = 0;
        for (int i = 1; i < altitudes.Count; i++)
        {
            gain += Math.Max(0, altitudes[i] - altitudes[i - 1]);
        }

        return gain;
    }

    /// <summary>
    /// A deterministic pseudo-random sequence in +/- <paramref name="amplitudeMeters"/>.
    /// A seeded <see cref="Random"/> is not contractually stable across runtime
    /// versions, and the assertion above is tight enough that a shifted sequence
    /// would read as a regression in the filter rather than as a changed fixture.
    /// </summary>
    private static double[] SensorNoise(int count, double amplitudeMeters)
    {
        var values = new double[count];
        uint state = 12345;

        for (int i = 0; i < count; i++)
        {
            state = (state * 1664525u) + 1013904223u;
            values[i] = ((((state >> 8) / (double)(1 << 24)) * 2.0) - 1.0) * amplitudeMeters;
        }

        return values;
    }
}
