using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Cadence.Application.Abstractions;
using Cadence.Domain.Activities;
using Cadence.Domain.Analytics;

namespace Cadence.Infrastructure.Parsing;

/// <summary>
/// Reads GPS Exchange Format tracks.
///
/// Every element is matched on its local name. GPX exists in a 1.1 and a 1.0
/// namespace, third-party exporters routinely emit it with no namespace at all,
/// and the Garmin track point extensions turn up under at least three different
/// prefixes (<c>gpxtpx</c>, <c>ns3</c>, <c>gpxdata</c>). Binding to namespace
/// URIs would reject a large share of real files for no benefit.
/// </summary>
public sealed class GpxActivityParser : IActivityFileParser
{
    private const string FileExtension = ".gpx";

    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    public SourceFormat Format => SourceFormat.Gpx;

    public bool CanParse(string fileName, ReadOnlySpan<byte> header)
    {
        if (!string.IsNullOrEmpty(fileName)
            && Path.GetExtension(fileName).Equals(FileExtension, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (header.StartsWith(Utf8Bom))
        {
            header = header[Utf8Bom.Length..];
        }

        foreach (byte value in header)
        {
            if (value is 0x20 or 0x09 or 0x0A or 0x0D)
            {
                continue;
            }

            return value == (byte)'<';
        }

        return false;
    }

    public async Task<ParsedActivity> ParseAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        XmlReaderSettings settings = new()
        {
            Async = true,

            // An uploaded file is untrusted: no DTDs, no external entities.
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
        };

        using XmlReader reader = XmlReader.Create(stream, settings);
        XDocument document = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken)
            .ConfigureAwait(false);

        XElement root = document.Root
            ?? throw new InvalidDataException("The GPX document is empty.");

        if (!Matches(root.Name.LocalName, "gpx"))
        {
            throw new InvalidDataException(
                $"Expected a <gpx> root element but found <{root.Name.LocalName}>.");
        }

        string? metadataName = FirstChild(root, "metadata") is { } metadata
            ? TrimmedValue(FirstChild(metadata, "name"))
            : null;

        string? trackName = null;
        string? trackType = null;
        List<RawPoint> raw = [];

        // Multiple tracks and segments concatenate in document order: a device
        // that lost its fix mid-run writes one track with several segments, and
        // a merged export writes several tracks.
        foreach (XElement track in Children(root, "trk"))
        {
            trackName ??= TrimmedValue(FirstChild(track, "name"));
            trackType ??= TrimmedValue(FirstChild(track, "type"));

            bool hasSegment = false;
            foreach (XElement segment in Children(track, "trkseg"))
            {
                hasSegment = true;
                ReadPoints(segment, raw);
            }

            // Some exporters drop the <trkseg> wrapper entirely.
            if (!hasSegment)
            {
                ReadPoints(track, raw);
            }
        }

        if (raw.Count == 0)
        {
            throw new InvalidDataException("The GPX document contains no <trkpt> elements.");
        }

        return new ParsedActivity(
            ResolveTimestamps(raw),
            MapSport(trackType),
            trackName ?? metadataName,
            SourceFormat.Gpx,
            Attribute(root, "creator"));
    }

    private static void ReadPoints(XElement parent, List<RawPoint> destination)
    {
        foreach (XElement element in Children(parent, "trkpt"))
        {
            if (ReadPoint(element) is { } point)
            {
                destination.Add(point);
            }
        }
    }

    private static RawPoint? ReadPoint(XElement element)
    {
        double? latitude = ParseDouble(Attribute(element, "lat"));
        double? longitude = ParseDouble(Attribute(element, "lon"));

        // A track point without coordinates violates the schema and cannot be
        // placed; dropping it is better than inventing a position.
        if (latitude is not { } lat || longitude is not { } lon)
        {
            return null;
        }

        DateTimeOffset? timestamp = null;
        double? altitude = null;
        PointChannels channels = default;

        foreach (XElement child in element.Elements())
        {
            string name = child.Name.LocalName;

            if (Matches(name, "ele"))
            {
                altitude ??= ParseDouble(child.Value);
            }
            else if (Matches(name, "time"))
            {
                timestamp ??= ParseTimestamp(child.Value);
            }
            else if (Matches(name, "extensions"))
            {
                // Extension payloads nest a wrapper element (TrackPointExtension)
                // around the values, and the wrapper differs between vendors.
                foreach (XElement extension in child.Descendants())
                {
                    channels.Apply(extension);
                }
            }
            else
            {
                // GPX 1.0 puts speed and course directly on the track point.
                channels.Apply(child);
            }
        }

        return new RawPoint(timestamp, lat, lon, altitude, channels);
    }

    /// <summary>
    /// Fills in the gaps left by files that timestamp only some of their points.
    /// Untimed points inherit the preceding timestamp; leading untimed points
    /// inherit the first known one.
    /// </summary>
    private static IReadOnlyList<TrackPoint> ResolveTimestamps(List<RawPoint> raw)
    {
        DateTimeOffset? firstKnown = null;
        foreach (RawPoint point in raw)
        {
            if (point.Timestamp is { } value)
            {
                firstKnown = value;
                break;
            }
        }

        if (firstKnown is not { } seed)
        {
            throw new InvalidDataException(
                "The GPX document has no <time> elements; a route without timestamps is not an activity.");
        }

        TrackPoint[] points = new TrackPoint[raw.Count];
        DateTimeOffset previous = seed;

        for (int i = 0; i < raw.Count; i++)
        {
            RawPoint source = raw[i];
            DateTimeOffset timestamp = source.Timestamp ?? previous;
            previous = timestamp;

            points[i] = new TrackPoint(
                Timestamp: timestamp,
                Latitude: source.Latitude,
                Longitude: source.Longitude,
                AltitudeMeters: source.Altitude,
                HeartRateBpm: source.Channels.HeartRate,
                CadenceRpm: source.Channels.Cadence,
                PowerWatts: source.Channels.Power,
                SpeedMetersPerSecond: source.Channels.Speed,
                CumulativeDistanceMeters: source.Channels.Distance,
                TemperatureCelsius: source.Channels.Temperature);
        }

        return points;
    }

    private static Sport MapSport(string? trackType)
    {
        if (string.IsNullOrWhiteSpace(trackType))
        {
            return Sport.Unknown;
        }

        string normalized = string.Concat(trackType.Where(char.IsLetterOrDigit)).ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return Sport.Unknown;
        }

        if (int.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out int code))
        {
            return MapNumericType(code);
        }

        bool running = normalized.Contains("run", StringComparison.Ordinal)
            || normalized.Contains("jog", StringComparison.Ordinal);

        if (running)
        {
            return normalized.Contains("trail", StringComparison.Ordinal) ? Sport.TrailRunning : Sport.Running;
        }

        bool cycling = normalized.Contains("bike", StringComparison.Ordinal)
            || normalized.Contains("bicycl", StringComparison.Ordinal)
            || normalized.Contains("cycl", StringComparison.Ordinal)
            || normalized.Contains("ride", StringComparison.Ordinal);

        if (cycling)
        {
            bool offRoad = normalized.Contains("mountain", StringComparison.Ordinal)
                || normalized.Contains("mtb", StringComparison.Ordinal)
                || normalized.Contains("offroad", StringComparison.Ordinal)
                || normalized.Contains("gravel", StringComparison.Ordinal);

            return offRoad ? Sport.MountainBiking : Sport.Cycling;
        }

        if (normalized.Contains("mtb", StringComparison.Ordinal))
        {
            return Sport.MountainBiking;
        }

        if (normalized.Contains("swim", StringComparison.Ordinal))
        {
            return Sport.Swimming;
        }

        if (normalized.Contains("hik", StringComparison.Ordinal))
        {
            return Sport.Hiking;
        }

        if (normalized.Contains("walk", StringComparison.Ordinal))
        {
            return Sport.Walking;
        }

        if (normalized.Contains("row", StringComparison.Ordinal))
        {
            return Sport.Rowing;
        }

        if (normalized.Contains("ski", StringComparison.Ordinal))
        {
            return Sport.Skiing;
        }

        return Sport.Unknown;
    }

    /// <summary>
    /// Strava writes its internal activity-type number into <c>&lt;type&gt;</c>
    /// instead of a word, and those files are a large fraction of the GPX in the
    /// wild. Only the codes that map cleanly onto the domain enum are listed.
    /// </summary>
    private static Sport MapNumericType(int code) => code switch
    {
        1 or 17 => Sport.Cycling,
        2 or 3 or 7 => Sport.Skiing,
        4 => Sport.Hiking,
        8 => Sport.Rowing,
        9 => Sport.Running,
        10 or 18 => Sport.Walking,
        16 => Sport.Swimming,
        _ => Sport.Unknown,
    };

    private static bool Matches(string actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<XElement> Children(XElement parent, string name) =>
        parent.Elements().Where(element => Matches(element.Name.LocalName, name));

    private static XElement? FirstChild(XElement parent, string name) =>
        Children(parent, name).FirstOrDefault();

    private static string? Attribute(XElement element, string name)
    {
        foreach (XAttribute attribute in element.Attributes())
        {
            if (Matches(attribute.Name.LocalName, name))
            {
                string value = attribute.Value.Trim();
                return value.Length == 0 ? null : value;
            }
        }

        return null;
    }

    private static string? TrimmedValue(XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        string value = element.Value.Trim();
        return value.Length == 0 ? null : value;
    }

    private static double? ParseDouble(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            && !double.IsNaN(value)
            && !double.IsInfinity(value)
                ? value
                : null;
    }

    private static int? ParseInt(string? text, int minimum)
    {
        if (ParseDouble(text) is not { } value)
        {
            return null;
        }

        double rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        return rounded >= minimum && rounded <= int.MaxValue ? (int)rounded : null;
    }

    private static DateTimeOffset? ParseTimestamp(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // GPX declares its times as UTC, so a value that carries no offset is
        // UTC rather than the server's local time. A value that does carry one
        // keeps it: that offset is the athlete's time zone.
        return DateTimeOffset.TryParse(
            text.Trim(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out DateTimeOffset value)
                ? value
                : null;
    }

    private readonly record struct RawPoint(
        DateTimeOffset? Timestamp,
        double Latitude,
        double Longitude,
        double? Altitude,
        PointChannels Channels);

    /// <summary>
    /// The non-positional channels of one track point, wherever they were found:
    /// a direct child in GPX 1.0, or somewhere inside <c>&lt;extensions&gt;</c>.
    /// The first value wins, so a vendor wrapper cannot overwrite a value the
    /// file already stated plainly.
    /// </summary>
    private struct PointChannels
    {
        public int? HeartRate;
        public int? Cadence;
        public int? Power;
        public double? Speed;
        public double? Distance;
        public double? Temperature;

        public void Apply(XElement element)
        {
            string name = element.Name.LocalName;

            if (Matches(name, "hr") || Matches(name, "heartrate"))
            {
                // A zero here means "no strap", not a stopped heart.
                HeartRate ??= ParseInt(element.Value, 1);
            }
            else if (Matches(name, "cad") || Matches(name, "cadence"))
            {
                Cadence ??= ParseInt(element.Value, 0);
            }
            else if (Matches(name, "power") || Matches(name, "powerinwatts") || Matches(name, "watts"))
            {
                Power ??= ParseInt(element.Value, 0);
            }
            else if (Matches(name, "atemp") || Matches(name, "temp") || Matches(name, "temperature"))
            {
                Temperature ??= ParseDouble(element.Value);
            }
            else if (Matches(name, "speed"))
            {
                Speed ??= ParseDouble(element.Value);
            }
            else if (Matches(name, "distance"))
            {
                Distance ??= ParseDouble(element.Value);
            }
        }
    }
}
