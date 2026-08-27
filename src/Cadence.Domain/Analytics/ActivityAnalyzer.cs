using Cadence.Domain.Geo;

namespace Cadence.Domain.Analytics;

/// <summary>
/// Turns a decoded track into the numbers the rest of the system reports.
///
/// Pure and synchronous: given the same points and options it returns the same
/// metrics, with no clock, no database, and no I/O. That is what makes the
/// interesting parts - moving time, elevation, splits - testable against
/// hand-built tracks instead of against a fixture file and a hope.
/// </summary>
public static class ActivityAnalyzer
{
    /// <summary>One segment between two consecutive samples.</summary>
    private readonly record struct Segment(
        double DistanceMeters,
        double RiseMeters,
        double Seconds,
        bool IsMoving,
        int EndIndex);

    public static ActivityMetrics Analyze(
        IReadOnlyList<TrackPoint> points,
        AnalysisOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(points);
        options ??= AnalysisOptions.Default;

        List<TrackPoint> ordered = [.. points.Where(p => p.HasPosition).OrderBy(p => p.Timestamp)];
        if (ordered.Count < 2)
        {
            return ActivityMetrics.Empty(ordered.Count == 1 ? ordered[0].Timestamp : default);
        }

        double[] smoothedAltitude = ElevationProfile
            .Compute(
                [.. ordered.Select(p => p.AltitudeMeters)],
                options.ElevationThresholdMeters,
                options.ElevationSmoothingWindow)
            .Smoothed;

        (List<Segment> segments, List<TrackPoint> cleaned, List<double> cumulative, int discarded) =
            BuildSegments(ordered, smoothedAltitude, options);

        if (cleaned.Count < 2)
        {
            return ActivityMetrics.Empty(ordered[0].Timestamp) with { DiscardedSampleCount = discarded };
        }

        // Elevation is recomputed over the cleaned series: points dropped as GPS
        // glitches often carry the worst altitude spikes too.
        ElevationProfile.Result elevation = ElevationProfile.Compute(
            [.. cleaned.Select(p => p.AltitudeMeters)],
            options.ElevationThresholdMeters,
            options.ElevationSmoothingWindow);

        double distance = cumulative[^1];
        TimeSpan elapsed = cleaned[^1].Timestamp - cleaned[0].Timestamp;
        TimeSpan moving = TimeSpan.FromSeconds(segments.Where(s => s.IsMoving).Sum(s => s.Seconds));

        // Moving time can legitimately exceed elapsed only through rounding;
        // clamp so a UI never shows 100.4% of the run spent moving.
        if (moving > elapsed)
        {
            moving = elapsed;
        }

        Pace averagePace = Pace.FromDistanceAndDuration(
            distance,
            moving > TimeSpan.Zero ? moving : elapsed);

        Pace gradeAdjusted = GradeAdjustedPace.OverSegments(
            segments.Where(s => s.IsMoving).Select(s => (s.DistanceMeters, s.RiseMeters, s.Seconds)));

        HeartRateZones? zones = ResolveZones(cleaned, options);

        return new ActivityMetrics
        {
            StartedAt = cleaned[0].Timestamp,
            ElapsedTime = elapsed,
            MovingTime = moving,
            DistanceMeters = distance,
            ElevationGainMeters = elevation.GainMeters,
            ElevationLossMeters = elevation.LossMeters,
            AveragePace = averagePace,
            GradeAdjustedPace = gradeAdjusted,
            AverageHeartRateBpm = TimeWeightedAverage(cleaned, p => p.HeartRateBpm, options),
            MaxHeartRateBpm = cleaned.Max(p => p.HeartRateBpm),
            AverageCadenceRpm = TimeWeightedAverage(cleaned, p => p.CadenceRpm, options),
            AveragePowerWatts = TimeWeightedAverage(cleaned, p => p.PowerWatts, options),
            Splits = ComputeSplits(cleaned, cumulative, segments, options),
            ZoneSeconds = zones?.Distribution(cleaned, options.MaximumSampleGap)
                ?? new Dictionary<HeartRateZone, double>(),
            CleanedPoints = cleaned,
            CumulativeDistanceMeters = cumulative,
            DiscardedSampleCount = discarded,
        };
    }

    private static (List<Segment> Segments, List<TrackPoint> Cleaned, List<double> Cumulative, int Discarded)
        BuildSegments(
            List<TrackPoint> ordered,
            double[] smoothedAltitude,
            AnalysisOptions options)
    {
        var segments = new List<Segment>(ordered.Count);
        var cleaned = new List<TrackPoint>(ordered.Count) { ordered[0] };
        var cumulative = new List<double>(ordered.Count) { 0 };

        int discarded = 0;
        double total = 0;
        int previous = 0;

        for (int i = 1; i < ordered.Count; i++)
        {
            TrackPoint from = ordered[previous];
            TrackPoint to = ordered[i];

            double seconds = (to.Timestamp - from.Timestamp).TotalSeconds;
            if (seconds <= 0)
            {
                // Duplicate or out-of-order timestamp: keep the first, drop the rest.
                discarded++;
                continue;
            }

            double distance = SegmentDistance(from, to);

            // Reject teleports before they can inflate the total. A dropped
            // sample is far cheaper than a phantom kilometre.
            if (seconds <= options.MaximumSampleGap.TotalSeconds
                && distance / seconds > options.MaximumPlausibleSpeedMetersPerSecond)
            {
                discarded++;
                continue;
            }

            bool withinGap = seconds <= options.MaximumSampleGap.TotalSeconds;
            double speed = to.SpeedMetersPerSecond ?? (distance / seconds);
            bool isMoving = withinGap && speed >= options.MovingSpeedThresholdMetersPerSecond;

            // Distance accrued across a long gap is not trustworthy: the athlete
            // may have driven home with the watch running.
            double countedDistance = withinGap ? distance : 0;
            total += countedDistance;

            double rise = smoothedAltitude.Length == ordered.Count
                ? smoothedAltitude[i] - smoothedAltitude[previous]
                : 0;

            segments.Add(new Segment(countedDistance, rise, withinGap ? seconds : 0, isMoving, i));
            cleaned.Add(to);
            cumulative.Add(total);
            previous = i;
        }

        return (segments, cleaned, cumulative, discarded);
    }

    /// <summary>
    /// Prefers the device's own cumulative distance, which comes from a wheel
    /// sensor or a footpod and is more accurate than differencing GPS fixes.
    /// Falls back to great-circle distance when it is absent or goes backwards.
    /// </summary>
    private static double SegmentDistance(TrackPoint from, TrackPoint to)
    {
        if (from.CumulativeDistanceMeters is { } start
            && to.CumulativeDistanceMeters is { } end
            && end >= start)
        {
            return end - start;
        }

        return GeoMath.HaversineDistance(from.Latitude, from.Longitude, to.Latitude, to.Longitude);
    }

    private static HeartRateZones? ResolveZones(IReadOnlyList<TrackPoint> points, AnalysisOptions options)
    {
        if (options.MaxHeartRate is > 0)
        {
            return HeartRateZones.ForAthlete(options.MaxHeartRate.Value, options.RestingHeartRate);
        }

        // No configured maximum: fall back to the highest rate actually observed.
        // Zones derived this way are indicative only, which the API makes clear.
        int? observed = points.Max(p => p.HeartRateBpm);
        return observed is > 0 ? HeartRateZones.ForAthlete(observed.Value, options.RestingHeartRate) : null;
    }

    private static int? TimeWeightedAverage(
        IReadOnlyList<TrackPoint> points,
        Func<TrackPoint, int?> selector,
        AnalysisOptions options)
    {
        double weighted = 0;
        double seconds = 0;

        for (int i = 0; i < points.Count - 1; i++)
        {
            int? value = selector(points[i]);
            if (value is not > 0)
            {
                continue;
            }

            double interval = (points[i + 1].Timestamp - points[i].Timestamp).TotalSeconds;
            if (interval <= 0 || interval > options.MaximumSampleGap.TotalSeconds)
            {
                continue;
            }

            weighted += value.Value * interval;
            seconds += interval;
        }

        return seconds > 0 ? (int)Math.Round(weighted / seconds) : null;
    }

    /// <summary>
    /// Splits at exact distance boundaries. The boundary almost never falls on a
    /// sample, so the crossing time is linearly interpolated within the segment
    /// that straddles it - otherwise every split is quantised to the sampling
    /// interval and consecutive splits visibly borrow seconds from each other.
    /// </summary>
    private static List<SplitResult> ComputeSplits(
        IReadOnlyList<TrackPoint> cleaned,
        IReadOnlyList<double> cumulative,
        IReadOnlyList<Segment> segments,
        AnalysisOptions options)
    {
        var splits = new List<SplitResult>();
        double splitLength = options.SplitDistanceMeters;
        if (splitLength <= 0 || cumulative.Count < 2)
        {
            return splits;
        }

        var segmentByEndIndex = new Dictionary<int, Segment>(segments.Count);
        foreach (Segment segment in segments)
        {
            segmentByEndIndex[segment.EndIndex] = segment;
        }

        int number = 1;
        double boundary = splitLength;
        double splitStartDistance = 0;
        DateTimeOffset splitStartTime = cleaned[0].Timestamp;
        double rise = 0;
        double heartRateSeconds = 0;
        double heartRateWeighted = 0;
        var splitSegments = new List<(double Distance, double Rise, double Seconds)>();

        for (int i = 1; i < cleaned.Count; i++)
        {
            double previousDistance = cumulative[i - 1];
            double currentDistance = cumulative[i];
            double segmentSeconds = (cleaned[i].Timestamp - cleaned[i - 1].Timestamp).TotalSeconds;

            double segmentRise = segmentByEndIndex.Count > 0 && i - 1 < segments.Count
                ? segments[i - 1].RiseMeters
                : 0;

            int? bpm = cleaned[i - 1].HeartRateBpm;
            if (bpm is > 0 && segmentSeconds > 0 && segmentSeconds <= options.MaximumSampleGap.TotalSeconds)
            {
                heartRateWeighted += bpm.Value * segmentSeconds;
                heartRateSeconds += segmentSeconds;
            }

            while (currentDistance >= boundary && currentDistance > previousDistance)
            {
                double fraction = (boundary - previousDistance) / (currentDistance - previousDistance);
                DateTimeOffset crossing = cleaned[i - 1].Timestamp
                    + TimeSpan.FromSeconds(segmentSeconds * fraction);

                double partialRise = segmentRise * fraction;
                splitSegments.Add((boundary - splitStartDistance - SumDistance(splitSegments), partialRise, segmentSeconds * fraction));

                TimeSpan duration = crossing - splitStartTime;
                splits.Add(new SplitResult(
                    number,
                    splitLength,
                    duration,
                    Pace.FromDistanceAndDuration(splitLength, duration),
                    GradeAdjustedPace.OverSegments(splitSegments),
                    rise + Math.Max(0, partialRise),
                    heartRateSeconds > 0 ? (int)Math.Round(heartRateWeighted / heartRateSeconds) : null));

                number++;
                splitStartDistance = boundary;
                splitStartTime = crossing;
                boundary += splitLength;
                rise = 0;
                heartRateSeconds = 0;
                heartRateWeighted = 0;
                splitSegments.Clear();

                // Remaining part of this segment belongs to the next split.
                segmentRise -= partialRise;
                segmentSeconds -= segmentSeconds * fraction;
                previousDistance = splitStartDistance;
            }

            if (currentDistance > previousDistance)
            {
                splitSegments.Add((currentDistance - previousDistance, segmentRise, segmentSeconds));
            }

            rise += Math.Max(0, segmentRise);
        }

        double trailing = cumulative[^1] - splitStartDistance;
        if (trailing > 1)
        {
            TimeSpan duration = cleaned[^1].Timestamp - splitStartTime;
            splits.Add(new SplitResult(
                number,
                trailing,
                duration,
                Pace.FromDistanceAndDuration(trailing, duration),
                GradeAdjustedPace.OverSegments(splitSegments),
                rise,
                heartRateSeconds > 0 ? (int)Math.Round(heartRateWeighted / heartRateSeconds) : null)
            {
                IsComplete = false,
            });
        }

        return splits;
    }

    private static double SumDistance(List<(double Distance, double Rise, double Seconds)> segments)
    {
        double total = 0;
        foreach ((double distance, _, _) in segments)
        {
            total += distance;
        }

        return total;
    }
}
