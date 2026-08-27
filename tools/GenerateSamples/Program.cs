using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;

namespace Cadence.Tools.GenerateSamples;

internal static class Program
{
    private static int Main(string[] args)
    {
        string outputDirectory = args.Length > 0
            ? Path.GetFullPath(args[0])
            : Path.Combine(FindRepositoryRoot(), "samples");

        Directory.CreateDirectory(outputDirectory);

        foreach (RouteProfile profile in SampleRoutes.All)
        {
            IReadOnlyList<Sample> track = TrackGenerator.Generate(profile);
            string path = Path.Combine(outputDirectory, profile.FileName);

            switch (Path.GetExtension(profile.FileName).ToLowerInvariant())
            {
                case ".gpx":
                    GpxWriter.Write(path, profile, track);
                    break;
                case ".fit":
                    FitWriter.Write(path, profile, track);
                    break;
                default:
                    throw new InvalidOperationException($"No encoder for '{profile.FileName}'.");
            }

            Report(path, profile, track);
        }

        Console.WriteLine();
        Console.WriteLine($"Wrote {SampleRoutes.All.Count} sample files to {outputDirectory}");
        return 0;
    }

    /// <summary>
    /// Prints both elevation figures so the difference between them is visible
    /// without opening the file. The naive number is what a delta-sum reports;
    /// the filtered number is what Cadence reports. On the flat route they
    /// differ by two orders of magnitude, which is the whole point of that
    /// fixture.
    /// </summary>
    private static void Report(string path, RouteProfile profile, IReadOnlyList<Sample> track)
    {
        double naiveGain = 0;
        for (int i = 1; i < track.Count; i++)
        {
            double delta = track[i].AltitudeMeters - track[i - 1].AltitudeMeters;
            if (delta > 0)
            {
                naiveGain += delta;
            }
        }

        double distanceKm = track[^1].CumulativeDistanceMeters / 1000.0;
        TimeSpan duration = track[^1].Timestamp - track[0].Timestamp;
        int paceSecondsPerKm = (int)Math.Round(duration.TotalSeconds / distanceKm);

        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "{0,-38} {1,5} pts  {2,6:F2} km  {3}:{4:D2}/km  gain {5,5:F0} m (naive {6,6:F0} m)  {7}",
            Path.GetFileName(path),
            track.Count,
            distanceKm,
            paceSecondsPerKm / 60,
            paceSecondsPerKm % 60,
            FilteredGain(track),
            naiveGain,
            profile.Description));
    }

    /// <summary>
    /// The same two-stage smooth-then-ratchet filter the domain applies,
    /// re-implemented here only so the generator can print a comparison. It is
    /// not the production code path.
    /// </summary>
    private static double FilteredGain(IReadOnlyList<Sample> track)
    {
        const int window = 5;
        const double threshold = 3.0;
        int half = window / 2;

        double gain = 0;
        double reference = double.NaN;

        for (int i = 0; i < track.Count; i++)
        {
            int start = Math.Max(0, i - half);
            int end = Math.Min(track.Count - 1, i + half);

            double sum = 0;
            for (int j = start; j <= end; j++)
            {
                sum += track[j].AltitudeMeters;
            }

            double smoothed = sum / (end - start + 1);

            if (double.IsNaN(reference))
            {
                reference = smoothed;
                continue;
            }

            double delta = smoothed - reference;
            if (delta >= threshold)
            {
                gain += delta;
                reference = smoothed;
            }
            else if (delta <= -threshold)
            {
                reference = smoothed;
            }
        }

        return gain;
    }

    private static string FindRepositoryRoot()
    {
        // The build output lives several directories below the repository, and
        // the working directory depends on how `dotnet run` was invoked, so
        // anchor on the marker file rather than on either of them.
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Cadence.slnx")))
            {
                return directory.FullName;
            }
        }

        return Directory.GetCurrentDirectory();
    }
}

internal readonly record struct Waypoint(double Latitude, double Longitude, double ElevationMeters);

/// <summary>One generated second of recording, before it is encoded.</summary>
internal readonly record struct Sample(
    DateTimeOffset Timestamp,
    double Latitude,
    double Longitude,
    double AltitudeMeters,
    double CumulativeDistanceMeters,
    double SpeedMetersPerSecond,
    int? HeartRateBpm,
    int CadenceRpm,
    double TemperatureCelsius);

internal sealed record RouteProfile
{
    public required string FileName { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required IReadOnlyList<Waypoint> Waypoints { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Flat-ground speed the athlete would hold; slopes are derived from it.</summary>
    public required double BaseSpeedMetersPerSecond { get; init; }

    public required int MaxHeartRate { get; init; }

    public required int RestingHeartRate { get; init; }

    public required double TemperatureCelsius { get; init; }

    /// <summary>Seed for every noise source, so regenerating produces byte-identical fixtures.</summary>
    public required int Seed { get; init; }

    /// <summary>Standard deviation of the barometric altimeter noise, metres.</summary>
    public double AltitudeNoiseMeters { get; init; } = 0.8;

    /// <summary>Standard deviation of the horizontal position error, metres.</summary>
    public double PositionNoiseMeters { get; init; } = 2.4;

    /// <summary>
    /// Amplitude of the terrain undulation superimposed on the straight
    /// interpolation between waypoints. Zero produces a genuinely flat profile.
    /// </summary>
    public double UndulationMeters { get; init; } = 2.5;

    /// <summary>
    /// Seconds before the chest strap pairs. Samples in this window carry no
    /// heart rate at all, which is what exercises the decoder's invalid-value
    /// handling rather than letting it assume every field is always present.
    /// </summary>
    public int HeartRateAcquisitionSeconds { get; init; } = 12;

    public string GpxTrackType { get; init; } = "running";
}

internal static class SampleRoutes
{
    public static IReadOnlyList<RouteProfile> All { get; } =
    [
        new RouteProfile
        {
            FileName = "canmore-benchlands-trail-run.gpx",
            Name = "Canmore Benchlands loop",
            Description = "trail run, sustained climb then descent",
            Waypoints =
            [
                new(51.0787, -115.3860, 1420),
                new(51.0812, -115.3918, 1441),
                new(51.0839, -115.3975, 1470),
                new(51.0864, -115.4033, 1503),
                new(51.0886, -115.4094, 1541),
                new(51.0903, -115.4158, 1575),
                new(51.0918, -115.4221, 1608),
                new(51.0935, -115.4282, 1642),
                new(51.0961, -115.4249, 1621),
                new(51.0978, -115.4185, 1594),
                new(51.0982, -115.4118, 1566),
                new(51.0971, -115.4051, 1531),
                new(51.0949, -115.3989, 1497),
                new(51.0918, -115.3934, 1468),
                new(51.0871, -115.3893, 1442),
                new(51.0824, -115.3870, 1428),
                new(51.0787, -115.3860, 1420),
            ],
            StartedAt = new DateTimeOffset(2026, 5, 17, 13, 42, 0, TimeSpan.Zero),
            BaseSpeedMetersPerSecond = 3.15,
            MaxHeartRate = 188,
            RestingHeartRate = 46,
            TemperatureCelsius = 11.5,
            UndulationMeters = 3.5,
            Seed = 20260517,
            GpxTrackType = "trail_running",
        },
        new RouteProfile
        {
            FileName = "bow-river-pathway-easy-run.gpx",
            Name = "Bow River pathway out and back",
            Description = "flat river path, altimeter noise only",
            Waypoints = OutAndBack(
            [
                new(51.0498, -114.0712, 1043),
                new(51.0521, -114.0651, 1042),
                new(51.0539, -114.0587, 1041),
                new(51.0552, -114.0521, 1041),
                new(51.0561, -114.0454, 1040),
            ]),
            StartedAt = new DateTimeOffset(2026, 5, 19, 12, 5, 0, TimeSpan.Zero),
            BaseSpeedMetersPerSecond = 3.35,
            MaxHeartRate = 188,
            RestingHeartRate = 46,
            TemperatureCelsius = 17.0,
            UndulationMeters = 0.0,
            AltitudeNoiseMeters = 0.9,
            Seed = 20260519,
        },
        new RouteProfile
        {
            FileName = "nose-hill-tempo-run.fit",
            Name = "Nose Hill tempo",
            Description = "rolling park loop, binary FIT fixture",
            Waypoints =
            [
                new(51.1042, -114.1128, 1163),
                new(51.1068, -114.1071, 1181),
                new(51.1091, -114.1009, 1204),
                new(51.1109, -114.0944, 1223),
                new(51.1121, -114.0876, 1236),
                new(51.1128, -114.0808, 1229),
                new(51.1119, -114.0742, 1211),
                new(51.1098, -114.0689, 1192),
                new(51.1069, -114.0658, 1178),
                new(51.1038, -114.0681, 1186),
                new(51.1015, -114.0733, 1201),
                new(51.1002, -114.0796, 1218),
                new(51.1001, -114.0862, 1231),
                new(51.1011, -114.0928, 1220),
                new(51.1022, -114.0995, 1198),
                new(51.1032, -114.1063, 1177),
                new(51.1042, -114.1128, 1163),
            ],
            StartedAt = new DateTimeOffset(2026, 5, 21, 17, 20, 0, TimeSpan.Zero),
            BaseSpeedMetersPerSecond = 3.85,
            MaxHeartRate = 188,
            RestingHeartRate = 46,
            TemperatureCelsius = 21.0,
            UndulationMeters = 2.0,
            Seed = 20260521,
        },
    ];

    /// <summary>
    /// Appends the reversed outbound leg. The turnaround waypoint is not
    /// repeated, so the two legs meet rather than producing a zero-length
    /// segment that would divide by zero when a gradient is taken.
    /// </summary>
    private static IReadOnlyList<Waypoint> OutAndBack(IReadOnlyList<Waypoint> outbound)
    {
        var full = new List<Waypoint>(outbound);
        for (int i = outbound.Count - 2; i >= 0; i--)
        {
            full.Add(outbound[i]);
        }

        return full;
    }
}

internal static class TrackGenerator
{
    private const double SampleIntervalSeconds = 1.0;

    /// <summary>Gradient is measured over this distance, not between adjacent samples.</summary>
    private const double GradientLookaheadMeters = 15.0;

    /// <summary>
    /// AR(1) coefficient for the noise sources. Real GPS and barometric error is
    /// strongly autocorrelated - it wanders over tens of seconds rather than
    /// resampling independently every second - and white noise would be far too
    /// easy for a smoothing filter to remove.
    /// </summary>
    private const double NoisePersistence = 0.85;

    public static IReadOnlyList<Sample> Generate(RouteProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var path = new PolylinePath(profile.Waypoints, profile.UndulationMeters);
        var random = new Random(profile.Seed);

        var samples = new List<Sample>(4096);

        double distance = 0;
        double speed = profile.BaseSpeedMetersPerSecond * 0.55;
        double heartRate = profile.RestingHeartRate + 18.0;

        double altitudeError = 0;
        double eastingError = 0;
        double northingError = 0;

        int second = 0;

        while (distance < path.TotalDistanceMeters)
        {
            double lookahead = Math.Min(distance + GradientLookaheadMeters, path.TotalDistanceMeters);
            double rise = path.ElevationAt(lookahead) - path.ElevationAt(distance);
            double run = Math.Max(lookahead - distance, 1e-3);
            double gradient = Math.Clamp(rise / run, -0.45, 0.45);
            double costFactor = Minetti.AdjustmentFactor(gradient);

            // Effort is held roughly constant, so the slope sets the speed. That
            // is what makes the grade-adjusted pace of this file interesting:
            // raw pace swings by more than a minute per kilometre while the
            // adjusted pace barely moves.
            double fatigue = 1.0 - (0.035 * (second / 3600.0));
            double targetSpeed = Math.Clamp(
                profile.BaseSpeedMetersPerSecond * fatigue / costFactor,
                1.15,
                profile.BaseSpeedMetersPerSecond * 1.45);

            speed += ((targetSpeed - speed) * 0.12) + (random.NextGaussian() * 0.045);
            speed = Math.Clamp(speed, 0.9, 6.5);

            distance = Math.Min(distance + (speed * SampleIntervalSeconds), path.TotalDistanceMeters);
            (double latitude, double longitude) = path.PositionAt(distance);
            double elevation = path.ElevationAt(distance);

            altitudeError = (altitudeError * NoisePersistence)
                + (random.NextGaussian() * profile.AltitudeNoiseMeters * Math.Sqrt(1 - (NoisePersistence * NoisePersistence)));
            eastingError = (eastingError * NoisePersistence)
                + (random.NextGaussian() * profile.PositionNoiseMeters * Math.Sqrt(1 - (NoisePersistence * NoisePersistence)));
            northingError = (northingError * NoisePersistence)
                + (random.NextGaussian() * profile.PositionNoiseMeters * Math.Sqrt(1 - (NoisePersistence * NoisePersistence)));

            (double noisyLatitude, double noisyLongitude) =
                Geo.Offset(latitude, longitude, eastingError, northingError);

            heartRate = NextHeartRate(profile, heartRate, speed * costFactor, second, random);
            int cadence = (int)Math.Round(Math.Clamp(84 + (5.5 * (speed - 3.0)) + (random.NextGaussian() * 1.1), 72, 98));

            samples.Add(new Sample(
                profile.StartedAt.AddSeconds(second),
                noisyLatitude,
                noisyLongitude,
                elevation + altitudeError,
                distance,
                speed,
                second < profile.HeartRateAcquisitionSeconds ? (int?)null : (int)Math.Round(heartRate),
                cadence,
                profile.TemperatureCelsius + (random.NextGaussian() * 0.25)));

            second++;
        }

        return samples;
    }

    /// <summary>
    /// First-order lag toward the effort-implied target plus cardiac drift.
    /// Heart rate trails a change in effort by roughly half a minute, and a
    /// series that tracks speed instantly is the giveaway of a synthetic file.
    /// </summary>
    private static double NextHeartRate(
        RouteProfile profile,
        double current,
        double equivalentFlatSpeed,
        int second,
        Random random)
    {
        const double referenceSpeed = 5.2;

        double intensity = Math.Clamp(equivalentFlatSpeed / referenceSpeed, 0.25, 1.05);
        double reserve = profile.MaxHeartRate - profile.RestingHeartRate;
        double drift = 6.0 * (second / 3600.0);

        double target = profile.RestingHeartRate
            + (reserve * Math.Clamp(0.22 + (0.80 * intensity), 0.30, 0.97))
            + drift;

        double next = current + ((target - current) * 0.035) + (random.NextGaussian() * 0.5);
        return Math.Clamp(next, profile.RestingHeartRate, profile.MaxHeartRate);
    }
}

/// <summary>
/// The waypoint list turned into something addressable by distance. Positions
/// interpolate linearly between waypoints; elevation adds a deterministic
/// undulation so the profile is not a sequence of perfectly constant gradients.
/// </summary>
internal sealed class PolylinePath
{
    private readonly IReadOnlyList<Waypoint> _waypoints;
    private readonly double[] _cumulative;
    private readonly double _undulationMeters;

    public PolylinePath(IReadOnlyList<Waypoint> waypoints, double undulationMeters)
    {
        ArgumentNullException.ThrowIfNull(waypoints);
        if (waypoints.Count < 2)
        {
            throw new ArgumentException("A route needs at least two waypoints.", nameof(waypoints));
        }

        _waypoints = waypoints;
        _undulationMeters = undulationMeters;
        _cumulative = new double[waypoints.Count];

        for (int i = 1; i < waypoints.Count; i++)
        {
            _cumulative[i] = _cumulative[i - 1] + Geo.HaversineDistance(
                waypoints[i - 1].Latitude,
                waypoints[i - 1].Longitude,
                waypoints[i].Latitude,
                waypoints[i].Longitude);
        }
    }

    public double TotalDistanceMeters => _cumulative[^1];

    public (double Latitude, double Longitude) PositionAt(double distanceMeters)
    {
        (int index, double t) = Locate(distanceMeters);
        Waypoint a = _waypoints[index];
        Waypoint b = _waypoints[index + 1];

        return (
            a.Latitude + ((b.Latitude - a.Latitude) * t),
            a.Longitude + ((b.Longitude - a.Longitude) * t));
    }

    public double ElevationAt(double distanceMeters)
    {
        (int index, double t) = Locate(distanceMeters);
        Waypoint a = _waypoints[index];
        Waypoint b = _waypoints[index + 1];

        double baseline = a.ElevationMeters + ((b.ElevationMeters - a.ElevationMeters) * t);
        if (_undulationMeters <= 0)
        {
            return baseline;
        }

        // Two incommensurate wavelengths, so the terrain never repeats over the
        // length of a route.
        return baseline
            + (_undulationMeters * Math.Sin(distanceMeters / 137.0))
            + (_undulationMeters * 0.45 * Math.Sin((distanceMeters / 41.0) + 1.3));
    }

    private (int Index, double T) Locate(double distanceMeters)
    {
        double clamped = Math.Clamp(distanceMeters, 0, TotalDistanceMeters);

        int index = Array.BinarySearch(_cumulative, clamped);
        if (index < 0)
        {
            index = ~index - 1;
        }

        index = Math.Clamp(index, 0, _cumulative.Length - 2);

        double segment = _cumulative[index + 1] - _cumulative[index];
        double t = segment > 1e-9 ? (clamped - _cumulative[index]) / segment : 0;

        return (index, Math.Clamp(t, 0, 1));
    }
}

/// <summary>
/// Minetti et al. (2002) cost of running. Duplicated from the domain on purpose:
/// see the note in GenerateSamples.csproj.
/// </summary>
internal static class Minetti
{
    private const double FlatCost = 3.6;

    public static double AdjustmentFactor(double gradient)
    {
        double i = Math.Clamp(gradient, -0.45, 0.45);
        double i2 = i * i;
        double i3 = i2 * i;
        double i4 = i3 * i;
        double i5 = i4 * i;

        double cost = (155.4 * i5)
            - (30.4 * i4)
            - (43.3 * i3)
            + (46.3 * i2)
            + (19.5 * i)
            + FlatCost;

        return cost / FlatCost;
    }
}

internal static class Geo
{
    public const double EarthRadiusMeters = 6_371_008.8;

    public static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        double phi1 = lat1 * Math.PI / 180.0;
        double phi2 = lat2 * Math.PI / 180.0;
        double deltaPhi = phi2 - phi1;
        double deltaLambda = (lon2 - lon1) * Math.PI / 180.0;

        double a = (Math.Sin(deltaPhi / 2) * Math.Sin(deltaPhi / 2))
            + (Math.Cos(phi1) * Math.Cos(phi2) * Math.Sin(deltaLambda / 2) * Math.Sin(deltaLambda / 2));

        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1.0, Math.Sqrt(a)));
    }

    /// <summary>Shifts a position by a local east/north offset in metres.</summary>
    public static (double Latitude, double Longitude) Offset(
        double latitude,
        double longitude,
        double eastingMeters,
        double northingMeters)
    {
        double latitudeDelta = northingMeters / EarthRadiusMeters * 180.0 / Math.PI;
        double longitudeDelta = eastingMeters
            / (EarthRadiusMeters * Math.Cos(latitude * Math.PI / 180.0))
            * 180.0
            / Math.PI;

        return (latitude + latitudeDelta, longitude + longitudeDelta);
    }
}

internal static class RandomExtensions
{
    /// <summary>Box-Muller. <see cref="Random.NextDouble"/> is uniform, and uniform noise looks wrong.</summary>
    public static double NextGaussian(this Random random)
    {
        ArgumentNullException.ThrowIfNull(random);

        double u1 = 1.0 - random.NextDouble();
        double u2 = random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }
}

internal static class GpxWriter
{
    private const string GpxNamespace = "http://www.topografix.com/GPX/1/1";
    private const string TrackPointExtensionNamespace =
        "http://www.garmin.com/xmlschemas/TrackPointExtension/v1";
    private const string XsiNamespace = "http://www.w3.org/2001/XMLSchema-instance";
    private const string Creator = "Cadence GenerateSamples";

    public static void Write(string path, RouteProfile profile, IReadOnlyList<Sample> track)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(track);

        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            NewLineChars = "\n",
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        using var writer = XmlWriter.Create(path, settings);

        writer.WriteStartDocument();
        writer.WriteStartElement("gpx", GpxNamespace);
        writer.WriteAttributeString("version", "1.1");
        writer.WriteAttributeString("creator", Creator);
        writer.WriteAttributeString("xmlns", "gpxtpx", null, TrackPointExtensionNamespace);
        writer.WriteAttributeString("xmlns", "xsi", null, XsiNamespace);
        writer.WriteAttributeString(
            "schemaLocation",
            XsiNamespace,
            $"{GpxNamespace} http://www.topografix.com/GPX/1/1/gpx.xsd " +
            $"{TrackPointExtensionNamespace} https://www8.garmin.com/xmlschemas/TrackPointExtensionv1.xsd");

        writer.WriteStartElement("metadata", GpxNamespace);
        writer.WriteElementString("name", GpxNamespace, profile.Name);
        writer.WriteElementString("time", GpxNamespace, Timestamp(profile.StartedAt));
        writer.WriteEndElement();

        writer.WriteStartElement("trk", GpxNamespace);
        writer.WriteElementString("name", GpxNamespace, profile.Name);
        writer.WriteElementString("type", GpxNamespace, profile.GpxTrackType);
        writer.WriteStartElement("trkseg", GpxNamespace);

        foreach (Sample sample in track)
        {
            writer.WriteStartElement("trkpt", GpxNamespace);
            writer.WriteAttributeString("lat", Number(sample.Latitude, 7));
            writer.WriteAttributeString("lon", Number(sample.Longitude, 7));

            writer.WriteElementString("ele", GpxNamespace, Number(sample.AltitudeMeters, 1));
            writer.WriteElementString("time", GpxNamespace, Timestamp(sample.Timestamp));

            writer.WriteStartElement("extensions", GpxNamespace);
            writer.WriteStartElement("gpxtpx", "TrackPointExtension", TrackPointExtensionNamespace);

            // A missing element, not a zero: nothing was measured, and a decoder
            // that reads absence as 0 bpm produces an average that is wrong for
            // the whole activity.
            if (sample.HeartRateBpm is { } bpm)
            {
                writer.WriteElementString(
                    "gpxtpx",
                    "hr",
                    TrackPointExtensionNamespace,
                    bpm.ToString(CultureInfo.InvariantCulture));
            }

            writer.WriteElementString(
                "gpxtpx",
                "cad",
                TrackPointExtensionNamespace,
                sample.CadenceRpm.ToString(CultureInfo.InvariantCulture));

            writer.WriteElementString(
                "gpxtpx",
                "atemp",
                TrackPointExtensionNamespace,
                Number(sample.TemperatureCelsius, 1));

            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static string Number(double value, int decimals) =>
        Math.Round(value, decimals).ToString($"F{decimals}", CultureInfo.InvariantCulture);

    // The Z is quoted so it is emitted as the literal UTC designator rather than
    // being read as a format specifier.
    private static string Timestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}

/// <summary>
/// Encodes a minimal but structurally complete FIT file: a 14-byte header,
/// file_id and sport messages, a record definition, one record message per
/// sample, and a trailing CRC.
///
/// Three local message types are live at once and one field is variable-length,
/// so a reader cannot get through this file by assuming a single fixed record
/// shape - it has to keep the definition table and honour the declared sizes.
///
/// Everything is little-endian because the architecture byte in each definition
/// message says so, and every multi-byte write below goes through the explicit
/// helpers rather than through BitConverter, so the encoding does not depend on
/// the machine that runs the generator.
/// </summary>
internal static class FitWriter
{
    private const byte HeaderSize = 14;

    /// <summary>Protocol 2.0, encoded as a packed major/minor nibble pair.</summary>
    private const byte ProtocolVersion = 0x20;

    /// <summary>Profile 21.40, encoded as major * 100 + minor.</summary>
    private const ushort ProfileVersion = 2140;

    private const ushort FileIdGlobalMessageNumber = 0;
    private const ushort SportGlobalMessageNumber = 12;
    private const ushort RecordGlobalMessageNumber = 20;

    // Three concurrently-live local types, which is the point: the reader has to
    // keep a table of definitions rather than assume one shape per file.
    private const byte FileIdLocalMessageType = 0;
    private const byte SportLocalMessageType = 1;
    private const byte RecordLocalMessageType = 2;

    /// <summary>sport.sport = 1 (running), sport.sub_sport = 0 (generic).</summary>
    private const byte SportRunning = 1;
    private const byte SubSportGeneric = 0;

    private const byte DefinitionMessageHeaderBit = 0x40;

    /// <summary>file_id.type = 4, "activity".</summary>
    private const byte ActivityFileType = 4;

    private const byte ManufacturerDevelopment = 255;

    // FIT base types. The high bit marks a type as endian-sensitive; the low
    // nibble is the type number.
    private const byte BaseTypeEnum = 0x00;
    private const byte BaseTypeUInt8 = 0x02;
    private const byte BaseTypeString = 0x07;
    private const byte BaseTypeUInt16 = 0x84;
    private const byte BaseTypeSInt32 = 0x85;
    private const byte BaseTypeUInt32 = 0x86;
    private const byte BaseTypeUInt32Z = 0x8C;

    /// <summary>
    /// FIT timestamps count seconds from 1989-12-31T00:00:00Z, not from the Unix
    /// epoch. The two differ by 631,065,600 seconds, and mixing them up dates
    /// every activity to 1989.
    /// </summary>
    private static readonly DateTimeOffset FitEpoch = new(1989, 12, 31, 0, 0, 0, TimeSpan.Zero);

    /// <summary>2^31 semicircles to 180 degrees.</summary>
    private const double SemicirclesPerDegree = 2147483648.0 / 180.0;

    private const byte InvalidUInt8 = 0xFF;
    private const ushort InvalidUInt16 = 0xFFFF;

    /// <summary>Altitude is stored as (metres + 500) * 5, so a uint16 covers -500 m to 12,607 m at 20 cm.</summary>
    private const double AltitudeOffsetMeters = 500.0;
    private const double AltitudeScale = 5.0;

    public static void Write(string path, RouteProfile profile, IReadOnlyList<Sample> track)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(track);

        byte[] sportName = EncodeString(profile.Name);

        var body = new List<byte>((track.Count * 23) + 128);

        WriteFileIdDefinition(body);
        WriteFileIdMessage(body, profile.StartedAt);
        WriteSportDefinition(body, (byte)sportName.Length);
        WriteSportMessage(body, sportName);
        WriteRecordDefinition(body);

        foreach (Sample sample in track)
        {
            WriteRecordMessage(body, sample);
        }

        var file = new List<byte>(body.Count + HeaderSize + 2);
        WriteHeader(file, body.Count);
        file.AddRange(body);

        // The trailing CRC covers the header as well as the data records, which
        // is why it cannot be computed until the header is in place.
        WriteUInt16(file, FitCrc.Compute(Bytes(file)));

        File.WriteAllBytes(path, file.ToArray());
    }

    private static void WriteHeader(List<byte> file, int dataSize)
    {
        file.Add(HeaderSize);
        file.Add(ProtocolVersion);
        WriteUInt16(file, ProfileVersion);
        WriteUInt32(file, (uint)dataSize);

        // The ".FIT" signature sits at offset 8, after the data size - not at
        // the start of the file, which is where a reader that assumes magic
        // bytes come first will look for it.
        file.AddRange(".FIT"u8.ToArray());

        // Header CRC covers the first twelve bytes only - itself excluded.
        WriteUInt16(file, FitCrc.Compute(Bytes(file)));
    }

    private static void WriteFileIdDefinition(List<byte> body)
    {
        body.Add((byte)(DefinitionMessageHeaderBit | FileIdLocalMessageType));
        body.Add(0); // Reserved.
        body.Add(0); // Architecture: little-endian.
        WriteUInt16(body, FileIdGlobalMessageNumber);
        body.Add(5); // Field count.

        WriteFieldDefinition(body, fieldNumber: 0, size: 1, baseType: BaseTypeEnum); // type
        WriteFieldDefinition(body, fieldNumber: 1, size: 2, baseType: BaseTypeUInt16); // manufacturer
        WriteFieldDefinition(body, fieldNumber: 2, size: 2, baseType: BaseTypeUInt16); // product
        WriteFieldDefinition(body, fieldNumber: 3, size: 4, baseType: BaseTypeUInt32Z); // serial_number
        WriteFieldDefinition(body, fieldNumber: 4, size: 4, baseType: BaseTypeUInt32); // time_created
    }

    private static void WriteFileIdMessage(List<byte> body, DateTimeOffset createdAt)
    {
        body.Add(FileIdLocalMessageType);
        body.Add(ActivityFileType);
        WriteUInt16(body, ManufacturerDevelopment);
        WriteUInt16(body, 0);
        WriteUInt32(body, 0xC0FFEE01);
        WriteUInt32(body, ToFitTimestamp(createdAt));
    }

    private static void WriteSportDefinition(List<byte> body, byte nameFieldSize)
    {
        body.Add((byte)(DefinitionMessageHeaderBit | SportLocalMessageType));
        body.Add(0);
        body.Add(0);
        WriteUInt16(body, SportGlobalMessageNumber);
        body.Add(3);

        WriteFieldDefinition(body, fieldNumber: 0, size: 1, baseType: BaseTypeEnum); // sport
        WriteFieldDefinition(body, fieldNumber: 1, size: 1, baseType: BaseTypeEnum); // sub_sport
        WriteFieldDefinition(body, fieldNumber: 3, size: nameFieldSize, baseType: BaseTypeString); // name
    }

    private static void WriteSportMessage(List<byte> body, byte[] name)
    {
        body.Add(SportLocalMessageType);
        body.Add(SportRunning);
        body.Add(SubSportGeneric);
        body.AddRange(name);
    }

    /// <summary>
    /// A FIT string is a fixed-size, null-terminated UTF-8 array, and the reader
    /// advances by the size declared in the definition message - so the
    /// terminator has to live inside the field, not after it. Sizing the field to
    /// the string plus one byte is therefore the whole of the encoding.
    /// </summary>
    private static byte[] EncodeString(string value)
    {
        const int maxFieldBytes = 64;

        byte[] utf8 = Encoding.UTF8.GetBytes(value);
        int length = Math.Min(utf8.Length, maxFieldBytes - 1);

        var field = new byte[length + 1];
        Array.Copy(utf8, field, length);
        return field;
    }

    private static void WriteRecordDefinition(List<byte> body)
    {
        body.Add((byte)(DefinitionMessageHeaderBit | RecordLocalMessageType));
        body.Add(0);
        body.Add(0);
        WriteUInt16(body, RecordGlobalMessageNumber);
        body.Add(8);

        WriteFieldDefinition(body, fieldNumber: 253, size: 4, baseType: BaseTypeUInt32); // timestamp
        WriteFieldDefinition(body, fieldNumber: 0, size: 4, baseType: BaseTypeSInt32); // position_lat
        WriteFieldDefinition(body, fieldNumber: 1, size: 4, baseType: BaseTypeSInt32); // position_long
        WriteFieldDefinition(body, fieldNumber: 2, size: 2, baseType: BaseTypeUInt16); // altitude
        WriteFieldDefinition(body, fieldNumber: 3, size: 1, baseType: BaseTypeUInt8); // heart_rate
        WriteFieldDefinition(body, fieldNumber: 4, size: 1, baseType: BaseTypeUInt8); // cadence
        WriteFieldDefinition(body, fieldNumber: 5, size: 4, baseType: BaseTypeUInt32); // distance
        WriteFieldDefinition(body, fieldNumber: 6, size: 2, baseType: BaseTypeUInt16); // speed
    }

    private static void WriteRecordMessage(List<byte> body, Sample sample)
    {
        body.Add(RecordLocalMessageType);

        WriteUInt32(body, ToFitTimestamp(sample.Timestamp));
        WriteSInt32(body, ToSemicircles(sample.Latitude));
        WriteSInt32(body, ToSemicircles(sample.Longitude));
        WriteUInt16(body, ToAltitude(sample.AltitudeMeters));

        // 0xFF is the invalid value for uint8, not a heart rate of 255. A decoder
        // that does not special-case it reports a 255 bpm average.
        body.Add(sample.HeartRateBpm is { } bpm && bpm is > 0 and < 255 ? (byte)bpm : InvalidUInt8);

        body.Add(sample.CadenceRpm is > 0 and < 255 ? (byte)sample.CadenceRpm : InvalidUInt8);

        WriteUInt32(body, (uint)Math.Round(Math.Max(sample.CumulativeDistanceMeters, 0) * 100.0));
        WriteUInt16(body, ToSpeed(sample.SpeedMetersPerSecond));
    }

    private static void WriteFieldDefinition(List<byte> body, byte fieldNumber, byte size, byte baseType)
    {
        body.Add(fieldNumber);
        body.Add(size);
        body.Add(baseType);
    }

    private static uint ToFitTimestamp(DateTimeOffset value) =>
        (uint)Math.Round((value.ToUniversalTime() - FitEpoch).TotalSeconds);

    private static int ToSemicircles(double degrees) =>
        (int)Math.Clamp(Math.Round(degrees * SemicirclesPerDegree), int.MinValue, int.MaxValue);

    private static ushort ToAltitude(double meters)
    {
        double raw = Math.Round((meters + AltitudeOffsetMeters) * AltitudeScale);
        return raw >= 0 && raw < InvalidUInt16 ? (ushort)raw : InvalidUInt16;
    }

    private static ushort ToSpeed(double metersPerSecond)
    {
        double raw = Math.Round(Math.Max(metersPerSecond, 0) * 1000.0);
        return raw < InvalidUInt16 ? (ushort)raw : InvalidUInt16;
    }

    private static void WriteUInt16(List<byte> buffer, ushort value)
    {
        buffer.Add((byte)(value & 0xFF));
        buffer.Add((byte)((value >> 8) & 0xFF));
    }

    private static void WriteUInt32(List<byte> buffer, uint value)
    {
        buffer.Add((byte)(value & 0xFF));
        buffer.Add((byte)((value >> 8) & 0xFF));
        buffer.Add((byte)((value >> 16) & 0xFF));
        buffer.Add((byte)((value >> 24) & 0xFF));
    }

    private static void WriteSInt32(List<byte> buffer, int value) => WriteUInt32(buffer, unchecked((uint)value));

    private static ReadOnlySpan<byte> Bytes(List<byte> buffer) => CollectionsMarshal.AsSpan(buffer);
}

/// <summary>
/// The FIT CRC-16. It is not a byte-at-a-time table lookup: the standard defines
/// a sixteen-entry table applied to the low nibble and then the high nibble of
/// every byte, and a conventional CRC-16 implementation - even one with the same
/// polynomial - produces a different value.
/// </summary>
internal static class FitCrc
{
    private static readonly ushort[] Table =
    [
        0x0000, 0xCC01, 0xD801, 0x1400, 0xF001, 0x3C01, 0x2801, 0xE401,
        0xA001, 0x6C01, 0x7801, 0xB401, 0x5000, 0x9C01, 0x8801, 0x4400,
    ];

    public static ushort Compute(ReadOnlySpan<byte> data)
    {
        ushort crc = 0;
        foreach (byte value in data)
        {
            crc = Update(crc, value);
        }

        return crc;
    }

    private static ushort Update(ushort crc, byte value)
    {
        ushort temp = Table[crc & 0xF];
        crc = (ushort)((crc >> 4) & 0x0FFF);
        crc = (ushort)(crc ^ temp ^ Table[value & 0xF]);

        temp = Table[crc & 0xF];
        crc = (ushort)((crc >> 4) & 0x0FFF);
        crc = (ushort)(crc ^ temp ^ Table[(value >> 4) & 0xF]);

        return crc;
    }
}
