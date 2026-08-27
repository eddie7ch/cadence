using Cadence.Application.Abstractions;
using Cadence.Domain.Activities;
using Cadence.Domain.Analytics;
using Cadence.Infrastructure.Parsing.Fit;

namespace Cadence.Infrastructure.Parsing;

/// <summary>
/// Decodes Garmin's FIT binary format into track points. The wire decoding lives
/// in <see cref="FitDecoder"/>; this class owns the profile semantics - which
/// fields matter, and the scaling that turns raw integers into metres, degrees
/// and seconds.
/// </summary>
public sealed class FitActivityParser : IActivityFileParser
{
    private const string FileExtension = ".fit";
    private const int SignatureOffset = 8;

    private static ReadOnlySpan<byte> Signature => ".FIT"u8;

    public SourceFormat Format => SourceFormat.Fit;

    public bool CanParse(string fileName, ReadOnlySpan<byte> header)
    {
        if (!string.IsNullOrEmpty(fileName)
            && Path.GetExtension(fileName).Equals(FileExtension, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return header.Length >= SignatureOffset + Signature.Length
            && header.Slice(SignatureOffset, Signature.Length).SequenceEqual(Signature);
    }

    public async Task<ParsedActivity> ParseAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // The decoder walks the file with a span, and a definition message can be
        // referenced by data messages arbitrarily far downstream, so the file is
        // buffered whole rather than decoded incrementally.
        int capacity = stream.CanSeek ? checked((int)Math.Min(stream.Length, int.MaxValue)) : 0;
        using MemoryStream buffer = capacity > 0 ? new MemoryStream(capacity) : new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        ActivityBuilder builder = new();
        FitDecoder.Decode(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), builder);

        return builder.Build();
    }

    private sealed class ActivityBuilder : IFitMessageSink
    {
        private const double SemicirclesToDegrees = 180.0 / 2147483648.0;
        private const double AltitudeScale = 5.0;
        private const double AltitudeOffsetMeters = 500.0;
        private const double SpeedScale = 1000.0;
        private const double DistanceScale = 100.0;
        private const int MaxUtcOffsetSeconds = 14 * 3600;

        private readonly List<TrackPoint> _points = [];

        private int? _sessionSport;
        private int? _sessionSubSport;
        private int? _lapSport;
        private int? _lapSubSport;
        private int? _sportMessageSport;
        private int? _sportMessageSubSport;
        private string? _sportName;
        private string? _productName;
        private long? _manufacturer;
        private long? _product;
        private long? _activityTimestamp;
        private long? _activityLocalTimestamp;

        public bool IsInterested(ushort globalMessageNumber) => globalMessageNumber switch
        {
            FitGlobalMessage.FileId => true,
            FitGlobalMessage.Sport => true,
            FitGlobalMessage.Session => true,
            FitGlobalMessage.Lap => true,
            FitGlobalMessage.Record => true,
            FitGlobalMessage.Activity => true,
            _ => false,
        };

        public void OnMessage(ushort globalMessageNumber, FitFieldSet fields)
        {
            switch (globalMessageNumber)
            {
                case FitGlobalMessage.Record:
                    AddRecord(fields);
                    break;

                case FitGlobalMessage.Session:
                    _sessionSport ??= fields.GetInt32(FitSessionField.Sport);
                    _sessionSubSport ??= fields.GetInt32(FitSessionField.SubSport);
                    break;

                case FitGlobalMessage.Lap:
                    _lapSport ??= fields.GetInt32(FitLapField.Sport);
                    _lapSubSport ??= fields.GetInt32(FitLapField.SubSport);
                    break;

                case FitGlobalMessage.Sport:
                    _sportMessageSport ??= fields.GetInt32(FitSportField.Sport);
                    _sportMessageSubSport ??= fields.GetInt32(FitSportField.SubSport);
                    _sportName ??= fields.GetString(FitSportField.Name);
                    break;

                case FitGlobalMessage.FileId:
                    _manufacturer ??= fields.GetInt64(FitFileIdField.Manufacturer);
                    _product ??= fields.GetInt64(FitFileIdField.Product);
                    _productName ??= fields.GetString(FitFileIdField.ProductName);
                    break;

                case FitGlobalMessage.Activity:
                    _activityTimestamp ??= fields.GetInt64(FitCommonField.Timestamp);
                    _activityLocalTimestamp ??= fields.GetInt64(FitActivityField.LocalTimestamp);
                    break;

                default:
                    break;
            }
        }

        public ParsedActivity Build()
        {
            if (_points.Count == 0)
            {
                throw new FitFormatException(
                    "The FIT file decoded successfully but contains no timestamped record messages.");
            }

            IReadOnlyList<TrackPoint> points = ApplyLocalUtcOffset(_points);

            return new ParsedActivity(points, ResolveSport(), _sportName, SourceFormat.Fit, ResolveDeviceName());
        }

        private static double? SemicirclesToDegreesOrNull(double? semicircles, double limit)
        {
            if (semicircles is not { } value)
            {
                return null;
            }

            double degrees = value * SemicirclesToDegrees;
            return Math.Abs(degrees) <= limit ? degrees : null;
        }

        private void AddRecord(FitFieldSet fields)
        {
            // A record without a timestamp cannot be placed on the time axis, and
            // the analytics layer works entirely in elapsed time.
            if (FitEpoch.ToDateTimeOffset(fields.GetDouble(FitCommonField.Timestamp)) is not { } timestamp)
            {
                return;
            }

            double? latitude = SemicirclesToDegreesOrNull(fields.GetDouble(FitRecordField.PositionLat), 90.0);
            double? longitude = SemicirclesToDegreesOrNull(fields.GetDouble(FitRecordField.PositionLong), 180.0);

            // Indoor sessions and pre-fix samples have no position. TrackPoint
            // models that as 0,0 and reports HasPosition false, so the sample is
            // still available for heart rate and cadence.
            bool hasPosition = latitude.HasValue && longitude.HasValue;

            double? altitude = fields.GetFirstDouble(FitRecordField.EnhancedAltitude, FitRecordField.Altitude) is { } rawAltitude
                ? (rawAltitude / AltitudeScale) - AltitudeOffsetMeters
                : null;

            double? speed = fields.GetFirstDouble(FitRecordField.EnhancedSpeed, FitRecordField.Speed) is { } rawSpeed
                ? rawSpeed / SpeedScale
                : null;

            double? distance = fields.GetDouble(FitRecordField.Distance) is { } rawDistance
                ? rawDistance / DistanceScale
                : null;

            int? heartRate = fields.GetInt32(FitRecordField.HeartRate);

            _points.Add(new TrackPoint(
                Timestamp: timestamp,
                Latitude: hasPosition ? latitude!.Value : 0.0,
                Longitude: hasPosition ? longitude!.Value : 0.0,
                AltitudeMeters: altitude,
                // Some devices write a literal 0 rather than the invalid sentinel
                // when the strap drops out; nobody has a heart rate of zero.
                HeartRateBpm: heartRate > 0 ? heartRate : null,
                CadenceRpm: fields.GetInt32(FitRecordField.Cadence),
                PowerWatts: fields.GetInt32(FitRecordField.Power),
                SpeedMetersPerSecond: speed,
                CumulativeDistanceMeters: distance,
                TemperatureCelsius: fields.GetDouble(FitRecordField.Temperature)));
        }

        /// <summary>
        /// The activity message carries the same instant twice, once as UTC and
        /// once as the device's local time. The difference is the only record of
        /// the athlete's time zone, and losing it turns an early morning run into
        /// a late evening one for anyone reading the timestamps.
        /// </summary>
        private IReadOnlyList<TrackPoint> ApplyLocalUtcOffset(List<TrackPoint> points)
        {
            if (_activityTimestamp is not { } utc || _activityLocalTimestamp is not { } local)
            {
                return points;
            }

            long offsetSeconds = local - utc;
            if (Math.Abs(offsetSeconds) > MaxUtcOffsetSeconds)
            {
                return points;
            }

            // Real offsets are whole minutes; anything else is clock drift baked
            // into the file.
            TimeSpan offset = TimeSpan.FromMinutes(Math.Round(offsetSeconds / 60.0, MidpointRounding.AwayFromZero));
            if (offset == TimeSpan.Zero)
            {
                return points;
            }

            TrackPoint[] shifted = new TrackPoint[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                shifted[i] = points[i] with { Timestamp = points[i].Timestamp.ToOffset(offset) };
            }

            return shifted;
        }

        private Sport ResolveSport()
        {
            // Session first: it is the file's own summary of what was recorded.
            // Laps and the standalone sport message are fallbacks for files that
            // were truncated before the session was written.
            Sport sport = FitSportMapper.Map(_sessionSport, _sessionSubSport);
            if (sport != Sport.Unknown)
            {
                return sport;
            }

            sport = FitSportMapper.Map(_lapSport, _lapSubSport);
            if (sport != Sport.Unknown)
            {
                return sport;
            }

            return FitSportMapper.Map(_sportMessageSport, _sportMessageSubSport);
        }

        private string? ResolveDeviceName()
        {
            if (!string.IsNullOrWhiteSpace(_productName))
            {
                return _productName;
            }

            string? manufacturer = FitManufacturer.Name(_manufacturer);

            if (manufacturer is null && _manufacturer is { } id)
            {
                manufacturer = $"Manufacturer {id}";
            }

            if (manufacturer is null)
            {
                return null;
            }

            return _product is { } product ? $"{manufacturer} {product}" : manufacturer;
        }
    }
}
