using Cadence.Domain.Analytics;
using Shouldly;
using Xunit;

namespace Cadence.UnitTests.Analytics;

public sealed class HeartRateZonesTests
{
    private static readonly DateTimeOffset Start = new(2026, 4, 12, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Percent_of_max_zones_sit_at_sixty_seventy_eighty_and_ninety_percent()
    {
        HeartRateZones zones = HeartRateZones.ForAthlete(maxHeartRate: 200);

        zones.UpperBounds.ShouldBe(new[] { 120, 140, 160, 180 });
        zones.MaxHeartRate.ShouldBe(200);
        zones.RestingHeartRate.ShouldBeNull();
    }

    [Theory]
    [InlineData(40, HeartRateZone.Zone1)]
    [InlineData(119, HeartRateZone.Zone1)]
    [InlineData(120, HeartRateZone.Zone2)]
    [InlineData(139, HeartRateZone.Zone2)]
    [InlineData(140, HeartRateZone.Zone3)]
    [InlineData(159, HeartRateZone.Zone3)]
    [InlineData(160, HeartRateZone.Zone4)]
    [InlineData(179, HeartRateZone.Zone4)]
    [InlineData(180, HeartRateZone.Zone5)]
    [InlineData(205, HeartRateZone.Zone5)]
    public void A_rate_on_a_zone_boundary_belongs_to_the_zone_it_opens(int bpm, HeartRateZone expected)
    {
        HeartRateZones.ForAthlete(maxHeartRate: 200).ZoneFor(bpm).ShouldBe(expected);
    }

    [Fact]
    public void Supplying_a_resting_rate_switches_the_zones_onto_heart_rate_reserve()
    {
        HeartRateZones percentOfMax = HeartRateZones.ForAthlete(maxHeartRate: 180);
        HeartRateZones reserve = HeartRateZones.ForAthlete(maxHeartRate: 180, restingHeartRate: 60);

        percentOfMax.UpperBounds.ShouldBe(new[] { 108, 126, 144, 162 });

        // Karvonen scales the fractions over the 120 bpm between rest and max,
        // which lifts every easy boundary well above the percent-of-max figure.
        reserve.UpperBounds.ShouldBe(new[] { 126, 138, 150, 162 });
        reserve.RestingHeartRate.ShouldBe(60);
    }

    [Fact]
    public void The_two_models_disagree_about_which_zone_the_same_rate_is_in()
    {
        HeartRateZones percentOfMax = HeartRateZones.ForAthlete(maxHeartRate: 180);
        HeartRateZones reserve = HeartRateZones.ForAthlete(maxHeartRate: 180, restingHeartRate: 60);

        percentOfMax.ZoneFor(130).ShouldBe(HeartRateZone.Zone3);
        reserve.ZoneFor(130).ShouldBe(HeartRateZone.Zone2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-40)]
    [InlineData(200)]
    [InlineData(250)]
    public void An_unusable_resting_rate_falls_back_to_percent_of_max(int restingHeartRate)
    {
        HeartRateZones zones = HeartRateZones.ForAthlete(200, restingHeartRate);

        zones.UpperBounds.ShouldBe(new[] { 120, 140, 160, 180 });
        zones.RestingHeartRate.ShouldBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void An_athlete_with_no_maximum_heart_rate_cannot_have_zones(int maxHeartRate)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => HeartRateZones.ForAthlete(maxHeartRate));
    }

    [Theory]
    [InlineData(30, 187)]
    [InlineData(40, 180)]
    [InlineData(20, 194)]
    public void Estimating_from_age_uses_Tanaka_rather_than_two_hundred_and_twenty_minus_age(
        int age,
        int expectedMax)
    {
        HeartRateZones.FromAge(age).MaxHeartRate.ShouldBe(expectedMax);
    }

    [Fact]
    public void Time_in_zone_is_weighted_by_the_interval_each_sample_covers()
    {
        HeartRateZones zones = HeartRateZones.ForAthlete(maxHeartRate: 200);

        // One easy sample held for a full minute, then thirty hard samples one
        // second apart. Counting samples would call this a hard session; counting
        // seconds, correctly, calls it twice as much easy as hard.
        List<TrackPoint> points = [Sample(0, bpm: 130)];
        for (int second = 60; second <= 90; second++)
        {
            points.Add(Sample(second, bpm: 190));
        }

        IReadOnlyDictionary<HeartRateZone, double> distribution =
            zones.Distribution(points, TimeSpan.FromSeconds(60));

        distribution[HeartRateZone.Zone2].ShouldBe(60, 1e-9);
        distribution[HeartRateZone.Zone5].ShouldBe(30, 1e-9);
        points.Count(p => p.HeartRateBpm == 190).ShouldBeGreaterThan(
            points.Count(p => p.HeartRateBpm == 130));
    }

    [Fact]
    public void An_interval_longer_than_the_allowed_gap_is_not_counted_as_time_in_a_zone()
    {
        HeartRateZones zones = HeartRateZones.ForAthlete(maxHeartRate: 200);

        // The watch kept running through a two-hour drive home; it was not two
        // hours of zone 2.
        List<TrackPoint> points = [Sample(0, bpm: 130), Sample(7200, bpm: 130)];

        zones.Distribution(points, TimeSpan.FromSeconds(60))
            .Values.Sum()
            .ShouldBe(0);
    }

    [Fact]
    public void Samples_without_a_heart_rate_contribute_nothing()
    {
        HeartRateZones zones = HeartRateZones.ForAthlete(maxHeartRate: 200);

        List<TrackPoint> points =
        [
            Sample(0, bpm: null),
            Sample(10, bpm: 0),
            Sample(20, bpm: 130),
            Sample(30, bpm: 130),
        ];

        IReadOnlyDictionary<HeartRateZone, double> distribution =
            zones.Distribution(points, TimeSpan.FromSeconds(60));

        distribution.Values.Sum().ShouldBe(10, 1e-9);
        distribution[HeartRateZone.Zone2].ShouldBe(10, 1e-9);
    }

    [Fact]
    public void Every_zone_is_present_in_a_distribution_even_when_it_was_never_entered()
    {
        HeartRateZones zones = HeartRateZones.ForAthlete(maxHeartRate: 200);

        IReadOnlyDictionary<HeartRateZone, double> distribution =
            zones.Distribution([Sample(0, bpm: 130), Sample(10, bpm: 130)], TimeSpan.FromSeconds(60));

        distribution.Count.ShouldBe(5);
        distribution[HeartRateZone.Zone1].ShouldBe(0);
        distribution[HeartRateZone.Zone3].ShouldBe(0);
        distribution[HeartRateZone.Zone4].ShouldBe(0);
        distribution[HeartRateZone.Zone5].ShouldBe(0);
    }

    [Fact]
    public void A_single_sample_covers_no_interval_and_so_covers_no_time()
    {
        HeartRateZones zones = HeartRateZones.ForAthlete(maxHeartRate: 200);

        zones.Distribution([Sample(0, bpm: 150)], TimeSpan.FromSeconds(60)).Values.Sum().ShouldBe(0);
        zones.Distribution([], TimeSpan.FromSeconds(60)).Values.Sum().ShouldBe(0);
    }

    [Fact]
    public void A_distribution_over_a_missing_track_is_a_programming_error()
    {
        HeartRateZones zones = HeartRateZones.ForAthlete(maxHeartRate: 200);

        Should.Throw<ArgumentNullException>(() => zones.Distribution(null!, TimeSpan.FromSeconds(60)));
    }

    private static TrackPoint Sample(int secondsFromStart, int? bpm) => new(
        Start.AddSeconds(secondsFromStart),
        51.0447,
        -114.0719,
        HeartRateBpm: bpm);
}
