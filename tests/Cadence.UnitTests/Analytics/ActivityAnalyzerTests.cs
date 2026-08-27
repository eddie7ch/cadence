using Cadence.Domain.Analytics;
using Cadence.UnitTests.TestData;
using Shouldly;
using Xunit;

namespace Cadence.UnitTests.Analytics;

public sealed class ActivityAnalyzerTests
{
    private static readonly DateTimeOffset Start = new(2026, 4, 12, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_steady_run_reports_the_distance_time_and_pace_it_was_run_at()
    {
        IReadOnlyList<TrackPoint> track = TrackBuilder
            .StartingAt(Start)
            .Move(3.0, TimeSpan.FromSeconds(1200))
            .Build();

        ActivityMetrics metrics = ActivityAnalyzer.Analyze(track);

        metrics.StartedAt.ShouldBe(Start);
        metrics.DistanceMeters.ShouldBe(3600, 0.5);
        metrics.ElapsedTime.ShouldBe(TimeSpan.FromSeconds(1200));
        metrics.MovingTime.ShouldBe(TimeSpan.FromSeconds(1200));
        metrics.AveragePace.SecondsPerKilometer.ShouldBe(1000.0 / 3.0, 0.01);
        metrics.DiscardedSampleCount.ShouldBe(0);
        metrics.CleanedPoints.Count.ShouldBe(1201);
        metrics.CumulativeDistanceMeters.Count.ShouldBe(metrics.CleanedPoints.Count);
    }

    [Fact]
    public void Standing_at_a_traffic_light_counts_as_elapsed_time_but_not_as_moving_time()
    {
        IReadOnlyList<TrackPoint> track = TrackBuilder
            .StartingAt(Start)
            .Move(3.0, TimeSpan.FromSeconds(300))
            .Pause(TimeSpan.FromSeconds(120))
            .Move(3.0, TimeSpan.FromSeconds(300))
            .Build();

        ActivityMetrics metrics = ActivityAnalyzer.Analyze(track);

        metrics.ElapsedTime.ShouldBe(TimeSpan.FromSeconds(720));
        metrics.MovingTime.ShouldBe(TimeSpan.FromSeconds(600));
        metrics.DistanceMeters.ShouldBe(1800, 0.5);

        // Pace is over moving time, so the two minutes at the lights must not
        // make the run look slower than it was.
        metrics.AveragePace.SecondsPerKilometer.ShouldBe(1000.0 / 3.0, 0.01);
    }

    [Fact]
    public void Moving_time_never_exceeds_elapsed_time()
    {
        IReadOnlyList<TrackPoint> track = TrackBuilder
            .StartingAt(Start)
            .Move(3.0, TimeSpan.FromSeconds(600))
            .Build();

        ActivityMetrics metrics = ActivityAnalyzer.Analyze(track);

        metrics.MovingTime.ShouldBeLessThanOrEqualTo(metrics.ElapsedTime);
    }

    [Fact]
    public void A_dropout_longer_than_the_allowed_gap_contributes_neither_distance_nor_moving_time()
    {
        // Ten minutes of silence and 600 m of displacement: the athlete may have
        // driven home with the watch running, and neither the metres nor the
        // minutes can be trusted.
        IReadOnlyList<TrackPoint> track = TrackBuilder
            .StartingAt(Start)
            .Move(3.0, TimeSpan.FromSeconds(100))
            .Jump(600, TimeSpan.FromMinutes(10))
            .Move(3.0, TimeSpan.FromSeconds(100))
            .Build();

        ActivityMetrics metrics = ActivityAnalyzer.Analyze(track);

        metrics.DistanceMeters.ShouldBe(600, 0.5);
        metrics.MovingTime.ShouldBe(TimeSpan.FromSeconds(200));
        metrics.ElapsedTime.ShouldBe(TimeSpan.FromSeconds(800));

        // The samples either side of the gap are real data; it is the interval
        // between them that is worthless, so neither is discarded.
        metrics.DiscardedSampleCount.ShouldBe(0);
    }

    [Fact]
    public void An_implausible_teleport_is_thrown_away_and_counted()
    {
        // One fix five kilometres off the route, a second after the last good
        // one. Kept, it would put ten kilometres on a two-hundred-metre run.
        IReadOnlyList<TrackPoint> track = TrackBuilder
            .StartingAt(Start)
            .Move(3.0, TimeSpan.FromSeconds(100))
            .Glitch(5000)
            .Move(3.0, TimeSpan.FromSeconds(100))
            .Build();

        ActivityMetrics metrics = ActivityAnalyzer.Analyze(track);

        metrics.DiscardedSampleCount.ShouldBe(1);
        metrics.CleanedPoints.Count.ShouldBe(track.Count - 1);
        metrics.DistanceMeters.ShouldBe(600, 5);
        metrics.DistanceMeters.ShouldBeLessThan(1000);
    }

    [Fact]
    public void Splits_land_on_exact_distance_boundaries_with_an_interpolated_crossing_time()
    {
        // 3 m/s sampled once a second: a kilometre falls a third of the way
        // through a sample, never on one.
        IReadOnlyList<TrackPoint> track = TrackBuilder
            .StartingAt(Start)
            .WithHeartRate(150)
            .Move(3.0, TimeSpan.FromSeconds(1200))
            .Build();

        ActivityMetrics metrics = ActivityAnalyzer.Analyze(track);

        List<SplitResult> complete = [.. metrics.Splits.Where(s => s.IsComplete)];

        complete.Count.ShouldBe(3);
        complete.Select(s => s.Number).ShouldBe(new[] { 1, 2, 3 });

        foreach (SplitResult split in complete)
        {
            split.DistanceMeters.ShouldBe(1000, 1e-9);

            // 333.33 s, not the 333 or 334 that a split quantised to the sample
            // rate would give - and consecutive splits would then have to borrow
            // seconds from one another to make the total add up.
            split.Duration.TotalSeconds.ShouldBe(1000.0 / 3.0, 0.02);
            split.Pace.SecondsPerKilometer.ShouldBe(1000.0 / 3.0, 0.02);
            split.AverageHeartRateBpm.ShouldBe(150);
        }

        // The three crossings tile the first 1,000 s exactly, with no drift.
        complete.Sum(s => s.Duration.TotalSeconds).ShouldBe(1000, 0.01);
    }

    [Fact]
    public void The_last_few_hundred_metres_are_reported_as_an_incomplete_split()
    {
        IReadOnlyList<TrackPoint> track = TrackBuilder
            .StartingAt(Start)
            .Move(3.0, TimeSpan.FromSeconds(1200))
            .Build();

        ActivityMetrics metrics = ActivityAnalyzer.Analyze(track);

        metrics.Splits.Count.ShouldBe(4);

        SplitResult trailing = metrics.Splits[^1];

        // 600 m in 200 s is the same 5:33/km as the rest of the run. Ranked as a
        // kilometre it would read as a 3:20 finishing sprint.
        trailing.IsComplete.ShouldBeFalse();
        trailing.Number.ShouldBe(4);
        trailing.DistanceMeters.ShouldBe(600, 0.5);
        trailing.Duration.TotalSeconds.ShouldBe(200, 0.02);
        trailing.Pace.SecondsPerKilometer.ShouldBe(1000.0 / 3.0, 0.02);
    }

    [Fact]
    public void Split_length_is_configurable_so_an_imperial_athlete_gets_mile_splits()
    {
        IReadOnlyList<TrackPoint> track = TrackBuilder
            .StartingAt(Start)
            .Move(3.0, TimeSpan.FromSeconds(1200))
            .Build();

        ActivityMetrics metrics = ActivityAnalyzer.Analyze(
            track,
            new AnalysisOptions { SplitDistanceMeters = Pace.MetersPerMile });

        metrics.Splits.Count.ShouldBe(3);
        metrics.Splits[0].DistanceMeters.ShouldBe(Pace.MetersPerMile, 1e-9);
        metrics.Splits[1].DistanceMeters.ShouldBe(Pace.MetersPerMile, 1e-9);
        metrics.Splits[2].IsComplete.ShouldBeFalse();
        metrics.Splits[2].DistanceMeters.ShouldBe(3600 - (2 * Pace.MetersPerMile), 0.5);
    }

    [Fact]
    public void The_odometer_on_the_device_is_believed_ahead_of_the_satellite_fixes()
    {
        // The footpod says twice as far as the GPS does. A wheel sensor or a
        // calibrated footpod beats differencing fixes, so its number is the one
        // that has to come out.
        IReadOnlyList<TrackPoint> withOdometer = TrackBuilder
            .StartingAt(Start)
            .WithDeviceDistance(scale: 2.0)
            .Move(3.0, TimeSpan.FromSeconds(200))
            .Build();

        IReadOnlyList<TrackPoint> satelliteOnly = TrackBuilder
            .StartingAt(Start)
            .Move(3.0, TimeSpan.FromSeconds(200))
            .Build();

        ActivityAnalyzer.Analyze(satelliteOnly).DistanceMeters.ShouldBe(600, 0.5);
        ActivityAnalyzer.Analyze(withOdometer).DistanceMeters.ShouldBe(1200, 0.5);
    }

    [Fact]
    public void An_odometer_that_runs_backwards_is_ignored_in_favour_of_the_fixes()
    {
        IReadOnlyList<TrackPoint> track =
        [
            .. TrackBuilder.StartingAt(Start).Move(3.0, TimeSpan.FromSeconds(10)).Build()
                .Select((p, i) => p with { CumulativeDistanceMeters = 100 - i }),
        ];

        // Every odometer delta here is negative, so all ten segments fall back to
        // the great-circle distance rather than subtracting from the total.
        ActivityAnalyzer.Analyze(track).DistanceMeters.ShouldBe(30, 0.1);
    }

    [Fact]
    public void A_real_climb_shows_up_as_elevation_gain_and_as_a_faster_grade_adjusted_pace()
    {
        IReadOnlyList<TrackPoint> track = TrackBuilder
            .StartingAt(Start)
            .Move(3.0, TimeSpan.FromSeconds(600), gradient: 0.05)
            .Build();

        ActivityMetrics metrics = ActivityAnalyzer.Analyze(track);

        metrics.ElevationGainMeters.ShouldBe(90, 4);
        metrics.ElevationLossMeters.ShouldBe(0);

        // Grinding up a 5% grade is worth a quicker pace than the watch showed.
        metrics.GradeAdjustedPace.SecondsPerKilometer
            .ShouldBeLessThan(metrics.AveragePace.SecondsPerKilometer);
    }

    [Fact]
    public void A_flat_run_has_the_same_grade_adjusted_pace_as_actual_pace()
    {
        IReadOnlyList<TrackPoint> track = TrackBuilder
            .StartingAt(Start)
            .Move(3.0, TimeSpan.FromSeconds(600))
            .Build();

        ActivityMetrics metrics = ActivityAnalyzer.Analyze(track);

        metrics.ElevationGainMeters.ShouldBe(0);
        metrics.GradeAdjustedPace.SecondsPerKilometer
            .ShouldBe(metrics.AveragePace.SecondsPerKilometer, 0.5);
    }

    [Fact]
    public void Heart_rate_and_cadence_are_averaged_over_time_and_carried_into_the_zones()
    {
        IReadOnlyList<TrackPoint> track = TrackBuilder
            .StartingAt(Start)
            .WithHeartRate(150)
            .WithCadence(88)
            .Move(3.0, TimeSpan.FromSeconds(1200))
            .Build();

        ActivityMetrics metrics = ActivityAnalyzer.Analyze(
            track,
            new AnalysisOptions { MaxHeartRate = 200 });

        metrics.AverageHeartRateBpm.ShouldBe(150);
        metrics.MaxHeartRateBpm.ShouldBe(150);
        metrics.AverageCadenceRpm.ShouldBe(88);
        metrics.AveragePowerWatts.ShouldBeNull();

        // 150 against a 200 max is zone 3, and all 1,200 intervals belong there.
        metrics.ZoneSeconds[HeartRateZone.Zone3].ShouldBe(1200, 1e-6);
        metrics.ZoneSeconds.Values.Sum().ShouldBe(1200, 1e-6);
    }

    [Fact]
    public void A_track_with_no_heart_rate_at_all_gets_no_zones_rather_than_invented_ones()
    {
        IReadOnlyList<TrackPoint> track = TrackBuilder
            .StartingAt(Start)
            .Move(3.0, TimeSpan.FromSeconds(60))
            .Build();

        ActivityMetrics metrics = ActivityAnalyzer.Analyze(track);

        metrics.AverageHeartRateBpm.ShouldBeNull();
        metrics.MaxHeartRateBpm.ShouldBeNull();
        metrics.ZoneSeconds.ShouldBeEmpty();
    }

    [Fact]
    public void Samples_with_no_satellite_fix_are_dropped_before_anything_is_measured()
    {
        List<TrackPoint> track =
            [.. TrackBuilder.StartingAt(Start).Move(3.0, TimeSpan.FromSeconds(10)).Build()];

        // Null island, which is where a receiver puts itself while it searches.
        track.Insert(5, new TrackPoint(Start.AddSeconds(4.5), 0, 0));

        ActivityMetrics metrics = ActivityAnalyzer.Analyze(track);

        metrics.CleanedPoints.Any(p => !p.HasPosition).ShouldBeFalse();
        metrics.CleanedPoints.Count.ShouldBe(11);
        metrics.DistanceMeters.ShouldBe(30, 0.1);
    }

    [Fact]
    public void Samples_are_sorted_by_time_and_a_repeated_timestamp_is_discarded()
    {
        IReadOnlyList<TrackPoint> ordered =
            TrackBuilder.StartingAt(Start).Move(3.0, TimeSpan.FromSeconds(10)).Build();

        // Arrives backwards and with one timestamp repeated: both things a real
        // upload does, neither of which may change the distance.
        List<TrackPoint> shuffled = [.. ordered.Reverse(), ordered[5]];

        ActivityMetrics metrics = ActivityAnalyzer.Analyze(shuffled);

        metrics.StartedAt.ShouldBe(Start);
        metrics.ElapsedTime.ShouldBe(TimeSpan.FromSeconds(10));
        metrics.DistanceMeters.ShouldBe(30, 0.1);
        metrics.DiscardedSampleCount.ShouldBe(1);
    }

    [Fact]
    public void A_track_too_short_to_measure_yields_empty_metrics_rather_than_a_crash()
    {
        ActivityMetrics fromNothing = ActivityAnalyzer.Analyze([]);

        fromNothing.DistanceMeters.ShouldBe(0);
        fromNothing.ElapsedTime.ShouldBe(TimeSpan.Zero);
        fromNothing.MovingTime.ShouldBe(TimeSpan.Zero);
        fromNothing.AveragePace.ShouldBe(Pace.Zero);
        fromNothing.Splits.ShouldBeEmpty();
        fromNothing.StartedAt.ShouldBe(default(DateTimeOffset));

        ActivityMetrics fromOnePoint =
            ActivityAnalyzer.Analyze([new TrackPoint(Start, 51.0447, -114.0719)]);

        fromOnePoint.StartedAt.ShouldBe(Start);
        fromOnePoint.DistanceMeters.ShouldBe(0);
        fromOnePoint.Splits.ShouldBeEmpty();
    }

    [Fact]
    public void Analysing_a_missing_track_is_a_programming_error()
    {
        Should.Throw<ArgumentNullException>(() => ActivityAnalyzer.Analyze(null!));
    }
}
