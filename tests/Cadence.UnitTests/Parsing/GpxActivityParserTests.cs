using System.Text;
using Cadence.Application.Abstractions;
using Cadence.Domain.Activities;
using Cadence.Infrastructure.Parsing;
using Shouldly;
using Xunit;

namespace Cadence.UnitTests.Parsing;

public sealed class GpxActivityParserTests
{
    private readonly GpxActivityParser _parser = new();

    [Fact]
    public void The_parser_declares_itself_as_the_one_that_handles_GPX()
    {
        _parser.Format.ShouldBe(SourceFormat.Gpx);
    }

    [Theory]
    [InlineData("morning-run.gpx", true)]
    [InlineData("MORNING-RUN.GPX", true)]
    [InlineData("ride.fit", false)]
    [InlineData("ride.tcx", false)]
    public void A_file_is_recognised_by_its_extension(string fileName, bool expected)
    {
        _parser.CanParse(fileName, ReadOnlySpan<byte>.Empty).ShouldBe(expected);
    }

    [Fact]
    public void A_file_with_the_wrong_extension_is_still_recognised_by_what_is_inside_it()
    {
        // Browsers and share sheets rename uploads freely; the bytes do not lie.
        ReadOnlySpan<byte> header = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><gpx version=\"1.1\""u8;

        _parser.CanParse("upload.bin", header).ShouldBeTrue();
    }

    [Fact]
    public async Task A_GPX_eleven_file_yields_its_points_in_order_with_position_altitude_and_time()
    {
        ParsedActivity parsed = await ParseAsync(GarminGpx11);

        parsed.Format.ShouldBe(SourceFormat.Gpx);
        parsed.Name.ShouldBe("Bow River Loop");
        parsed.Points.Count.ShouldBe(3);

        parsed.Points[0].Latitude.ShouldBe(51.0447, 1e-9);
        parsed.Points[0].Longitude.ShouldBe(-114.0719, 1e-9);
        parsed.Points[0].AltitudeMeters.ShouldBe(1045.2);
        parsed.Points[0].Timestamp.ShouldBe(new DateTimeOffset(2026, 4, 12, 13, 0, 0, TimeSpan.Zero));

        parsed.Points.Select(p => p.Timestamp).ShouldBeInOrder();
        parsed.Points[^1].Timestamp.ShouldBe(new DateTimeOffset(2026, 4, 12, 13, 0, 20, TimeSpan.Zero));
    }

    [Fact]
    public async Task Heart_rate_and_cadence_come_out_of_the_Garmin_TrackPointExtension()
    {
        ParsedActivity parsed = await ParseAsync(GarminGpx11);

        parsed.Points.Select(p => p.HeartRateBpm).ShouldBe(new int?[] { 142, 148, 151 });
        parsed.Points.Select(p => p.CadenceRpm).ShouldBe(new int?[] { 86, 87, 88 });
    }

    [Fact]
    public async Task The_creator_attribute_names_the_device_the_file_came_off()
    {
        ParsedActivity parsed = await ParseAsync(GarminGpx11);

        parsed.DeviceName.ShouldBe("Garmin Connect");
    }

    [Fact]
    public async Task The_track_type_becomes_the_sport()
    {
        ParsedActivity parsed = await ParseAsync(GarminGpx11);

        parsed.Sport.ShouldBe(Sport.Running);
    }

    [Fact]
    public async Task A_document_with_no_XML_namespace_at_all_still_parses()
    {
        // Plenty of exporters and hand-rolled converters omit the namespace.
        // Matching on it strictly would reject files every other tool accepts.
        ParsedActivity parsed = await ParseAsync(NamespacelessGpx);

        parsed.Points.Count.ShouldBe(2);
        parsed.Name.ShouldBe("No Namespace Here");
        parsed.Points[0].Latitude.ShouldBe(51.0447, 1e-9);
        parsed.Points[1].AltitudeMeters.ShouldBe(1048.0);
        parsed.Points[1].Timestamp.ShouldBe(new DateTimeOffset(2026, 4, 12, 13, 0, 10, TimeSpan.Zero));
    }

    [Fact]
    public async Task Several_track_segments_are_concatenated_in_document_order()
    {
        // A watch starts a new segment every time it is paused. The segments are
        // one activity, and their order is the order they were recorded in.
        ParsedActivity parsed = await ParseAsync(TwoSegmentGpx);

        parsed.Points.Count.ShouldBe(4);
        parsed.Points.Select(p => p.Timestamp.Second).ShouldBe(new[] { 0, 10, 40, 50 });
        parsed.Points.Select(p => p.Timestamp).ShouldBeInOrder();
    }

    [Fact]
    public async Task A_point_with_no_elevation_or_extensions_parses_with_those_fields_missing()
    {
        ParsedActivity parsed = await ParseAsync(BareGpx);

        parsed.Points.Count.ShouldBe(2);
        parsed.Points[0].AltitudeMeters.ShouldBeNull();
        parsed.Points[0].HeartRateBpm.ShouldBeNull();
        parsed.Points[0].CadenceRpm.ShouldBeNull();
    }

    [Fact]
    public async Task Timestamps_from_another_offset_are_kept_as_the_same_instant()
    {
        ParsedActivity parsed = await ParseAsync(OffsetGpx);

        parsed.Points[0].Timestamp.ToUniversalTime()
            .ShouldBe(new DateTimeOffset(2026, 4, 12, 19, 0, 0, TimeSpan.Zero));
    }

    private async Task<ParsedActivity> ParseAsync(string document)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(document));

        return await _parser.ParseAsync(stream, CancellationToken.None);
    }

    private const string GarminGpx11 = """
        <?xml version="1.0" encoding="UTF-8"?>
        <gpx version="1.1" creator="Garmin Connect"
             xmlns="http://www.topografix.com/GPX/1/1"
             xmlns:gpxtpx="http://www.garmin.com/xmlschemas/TrackPointExtension/v1">
          <metadata>
            <time>2026-04-12T13:00:00Z</time>
          </metadata>
          <trk>
            <name>Bow River Loop</name>
            <type>running</type>
            <trkseg>
              <trkpt lat="51.0447" lon="-114.0719">
                <ele>1045.2</ele>
                <time>2026-04-12T13:00:00Z</time>
                <extensions>
                  <gpxtpx:TrackPointExtension>
                    <gpxtpx:hr>142</gpxtpx:hr>
                    <gpxtpx:cad>86</gpxtpx:cad>
                  </gpxtpx:TrackPointExtension>
                </extensions>
              </trkpt>
              <trkpt lat="51.0452" lon="-114.0719">
                <ele>1046.8</ele>
                <time>2026-04-12T13:00:10Z</time>
                <extensions>
                  <gpxtpx:TrackPointExtension>
                    <gpxtpx:hr>148</gpxtpx:hr>
                    <gpxtpx:cad>87</gpxtpx:cad>
                  </gpxtpx:TrackPointExtension>
                </extensions>
              </trkpt>
              <trkpt lat="51.0457" lon="-114.0719">
                <ele>1048.1</ele>
                <time>2026-04-12T13:00:20Z</time>
                <extensions>
                  <gpxtpx:TrackPointExtension>
                    <gpxtpx:hr>151</gpxtpx:hr>
                    <gpxtpx:cad>88</gpxtpx:cad>
                  </gpxtpx:TrackPointExtension>
                </extensions>
              </trkpt>
            </trkseg>
          </trk>
        </gpx>
        """;

    private const string NamespacelessGpx = """
        <?xml version="1.0" encoding="UTF-8"?>
        <gpx version="1.1" creator="Handmade Exporter">
          <trk>
            <name>No Namespace Here</name>
            <trkseg>
              <trkpt lat="51.0447" lon="-114.0719">
                <ele>1045.0</ele>
                <time>2026-04-12T13:00:00Z</time>
              </trkpt>
              <trkpt lat="51.0452" lon="-114.0719">
                <ele>1048.0</ele>
                <time>2026-04-12T13:00:10Z</time>
              </trkpt>
            </trkseg>
          </trk>
        </gpx>
        """;

    private const string TwoSegmentGpx = """
        <?xml version="1.0" encoding="UTF-8"?>
        <gpx version="1.1" creator="Cadence Tests" xmlns="http://www.topografix.com/GPX/1/1">
          <trk>
            <name>Paused Twice</name>
            <trkseg>
              <trkpt lat="51.0447" lon="-114.0719">
                <time>2026-04-12T13:00:00Z</time>
              </trkpt>
              <trkpt lat="51.0452" lon="-114.0719">
                <time>2026-04-12T13:00:10Z</time>
              </trkpt>
            </trkseg>
            <trkseg>
              <trkpt lat="51.0457" lon="-114.0719">
                <time>2026-04-12T13:00:40Z</time>
              </trkpt>
              <trkpt lat="51.0462" lon="-114.0719">
                <time>2026-04-12T13:00:50Z</time>
              </trkpt>
            </trkseg>
          </trk>
        </gpx>
        """;

    private const string BareGpx = """
        <?xml version="1.0" encoding="UTF-8"?>
        <gpx version="1.1" creator="Cadence Tests" xmlns="http://www.topografix.com/GPX/1/1">
          <trk>
            <trkseg>
              <trkpt lat="51.0447" lon="-114.0719">
                <time>2026-04-12T13:00:00Z</time>
              </trkpt>
              <trkpt lat="51.0452" lon="-114.0719">
                <time>2026-04-12T13:00:10Z</time>
              </trkpt>
            </trkseg>
          </trk>
        </gpx>
        """;

    private const string OffsetGpx = """
        <?xml version="1.0" encoding="UTF-8"?>
        <gpx version="1.1" creator="Cadence Tests" xmlns="http://www.topografix.com/GPX/1/1">
          <trk>
            <trkseg>
              <trkpt lat="51.0447" lon="-114.0719">
                <time>2026-04-12T13:00:00-06:00</time>
              </trkpt>
              <trkpt lat="51.0452" lon="-114.0719">
                <time>2026-04-12T13:00:10-06:00</time>
              </trkpt>
            </trkseg>
          </trk>
        </gpx>
        """;
}
