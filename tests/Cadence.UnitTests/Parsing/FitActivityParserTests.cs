using Cadence.Domain.Activities;
using Cadence.Domain.Analytics;
using Cadence.Infrastructure.Parsing;
using Shouldly;
using Xunit;

namespace Cadence.UnitTests.Parsing;

/// <summary>
/// Round-trips the binary fixture in <c>samples/</c>.
///
/// That file is written by <c>tools/GenerateSamples</c>, which is a deliberately
/// independent implementation of the FIT specification with no code shared with
/// the decoder. A wrong constant on either side - the semicircle divisor, the FIT
/// epoch, an invalid-value sentinel - shows up here as a disagreement rather than
/// cancelling out, which is exactly what a self-consistent fixture could not catch.
/// </summary>
public sealed class FitActivityParserTests
{
    private static readonly string? FixturePath = FindFixture("nose-hill-tempo-run.fit");

    [Fact]
    public void The_binary_fixture_exists()
    {
        // A silently skipped decoder test is worse than no decoder test.
        FixturePath.ShouldNotBeNull(
            "samples/nose-hill-tempo-run.fit is missing. Run: dotnet run --project tools/GenerateSamples");
    }

    [Fact]
    public async Task Decodes_the_generated_fit_file()
    {
        await using var stream = File.OpenRead(FixturePath!);
        var parsed = await new FitActivityParser().ParseAsync(stream, CancellationToken.None);

        parsed.Format.ShouldBe(SourceFormat.Fit);
        parsed.Points.Count.ShouldBeGreaterThan(500);
    }

    [Fact]
    public async Task Positions_decode_from_semicircles_into_plausible_degrees()
    {
        var points = await ParseAsync();

        // The fixture is a loop in Nose Hill Park, Calgary. Getting the
        // semicircle divisor wrong puts these off by orders of magnitude, and
        // getting latitude and longitude the wrong way round puts them at sea.
        foreach (TrackPoint point in points)
        {
            point.Latitude.ShouldBeInRange(50.9, 51.3);
            point.Longitude.ShouldBeInRange(-114.4, -113.8);
        }
    }

    [Fact]
    public async Task Timestamps_use_the_fit_epoch_not_the_unix_epoch()
    {
        var points = await ParseAsync();

        // The two epochs are 631,065,600 seconds apart, so confusing them lands
        // the activity in 1989 or twenty years into the future.
        points[0].Timestamp.Year.ShouldBeInRange(2020, 2030);
        points[^1].Timestamp.ShouldBeGreaterThan(points[0].Timestamp);
    }

    [Fact]
    public async Task Samples_are_ordered_and_monotonic_in_time()
    {
        var points = await ParseAsync();

        for (int i = 1; i < points.Count; i++)
        {
            points[i].Timestamp.ShouldBeGreaterThanOrEqualTo(points[i - 1].Timestamp);
        }
    }

    [Fact]
    public async Task Optional_channels_survive_the_round_trip()
    {
        var points = await ParseAsync();

        // Any(...) rather than ShouldContain(...): Shouldly takes an expression
        // tree, and expression trees cannot hold pattern matches.
        points.Any(p => p.HeartRateBpm > 60 && p.HeartRateBpm < 220).ShouldBeTrue();
        points.Any(p => p.AltitudeMeters > 900 && p.AltitudeMeters < 1400).ShouldBeTrue();
        points.Any(p => p.CumulativeDistanceMeters > 0).ShouldBeTrue();
    }

    [Fact]
    public async Task Analysis_of_the_decoded_track_matches_the_generator_intent()
    {
        ActivityMetrics metrics = ActivityAnalyzer.Analyze(await ParseAsync());

        // The generator reports 7.42 km and 122 m of filtered gain for this file.
        metrics.DistanceMeters.ShouldBeInRange(7_000, 7_900);
        metrics.ElevationGainMeters.ShouldBeInRange(80, 180);
        metrics.MovingTime.ShouldBeGreaterThan(TimeSpan.FromMinutes(25));
        metrics.Splits.Count(s => s.IsComplete).ShouldBe(7);
    }

    [Fact]
    public async Task A_truncated_file_is_rejected_rather_than_half_decoded()
    {
        byte[] whole = await File.ReadAllBytesAsync(FixturePath!, CancellationToken.None);
        using var truncated = new MemoryStream(whole[..(whole.Length / 3)]);

        await Should.ThrowAsync<Exception>(async () =>
            await new FitActivityParser().ParseAsync(truncated, CancellationToken.None));
    }

    [Fact]
    public async Task A_file_without_the_fit_signature_is_rejected()
    {
        using var notFit = new MemoryStream("this is plainly not a FIT file at all"u8.ToArray());

        await Should.ThrowAsync<Exception>(async () =>
            await new FitActivityParser().ParseAsync(notFit, CancellationToken.None));
    }

    private static async Task<IReadOnlyList<TrackPoint>> ParseAsync()
    {
        await using var stream = File.OpenRead(FixturePath!);
        var parsed = await new FitActivityParser().ParseAsync(stream, CancellationToken.None);
        return parsed.Points;
    }

    /// <summary>Walks up from the test binary to the repository root.</summary>
    private static string? FindFixture(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "samples", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
