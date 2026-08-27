namespace Cadence.Domain.Analytics;

public enum HeartRateZone
{
    /// <summary>Below 60% of maximum - recovery.</summary>
    Zone1 = 1,

    /// <summary>60-70% - aerobic base.</summary>
    Zone2 = 2,

    /// <summary>70-80% - tempo.</summary>
    Zone3 = 3,

    /// <summary>80-90% - threshold.</summary>
    Zone4 = 4,

    /// <summary>90%+ - VO2 max and above.</summary>
    Zone5 = 5,
}

/// <summary>
/// Five-zone model as a fraction of maximum heart rate.
///
/// Percent-of-max is used rather than heart-rate reserve because it needs only
/// one measurement an athlete plausibly knows. If a resting rate is supplied the
/// Karvonen (reserve) method is used instead, which is more accurate for
/// well-trained athletes whose resting rate is far below the population mean.
/// </summary>
public sealed class HeartRateZones
{
    private static readonly double[] PercentOfMaxBounds = [0.60, 0.70, 0.80, 0.90];
    private static readonly double[] ReserveBounds = [0.55, 0.65, 0.75, 0.85];

    private readonly int[] _upperBounds;

    private HeartRateZones(int maxHeartRate, int? restingHeartRate, int[] upperBounds)
    {
        MaxHeartRate = maxHeartRate;
        RestingHeartRate = restingHeartRate;
        _upperBounds = upperBounds;
    }

    public int MaxHeartRate { get; }

    public int? RestingHeartRate { get; }

    /// <summary>Inclusive upper bound in bpm for zones 1 to 4; zone 5 is everything above.</summary>
    public IReadOnlyList<int> UpperBounds => _upperBounds;

    public static HeartRateZones ForAthlete(int maxHeartRate, int? restingHeartRate = null)
    {
        if (maxHeartRate <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxHeartRate),
                maxHeartRate,
                "Maximum heart rate must be positive.");
        }

        bool useReserve = restingHeartRate is > 0 && restingHeartRate < maxHeartRate;
        double[] fractions = useReserve ? ReserveBounds : PercentOfMaxBounds;

        var bounds = new int[fractions.Length];
        for (int i = 0; i < fractions.Length; i++)
        {
            bounds[i] = useReserve
                ? (int)Math.Round(restingHeartRate!.Value + (fractions[i] * (maxHeartRate - restingHeartRate.Value)))
                : (int)Math.Round(fractions[i] * maxHeartRate);
        }

        return new HeartRateZones(maxHeartRate, useReserve ? restingHeartRate : null, bounds);
    }

    /// <summary>
    /// Population fallback when the athlete has never recorded a maximum.
    /// Tanaka et al. (2001), 208 - 0.7 x age, which fits observed data far better
    /// than the folkloric 220 - age.
    /// </summary>
    public static HeartRateZones FromAge(int age, int? restingHeartRate = null) =>
        ForAthlete((int)Math.Round(208 - (0.7 * age)), restingHeartRate);

    public HeartRateZone ZoneFor(int beatsPerMinute)
    {
        for (int i = 0; i < _upperBounds.Length; i++)
        {
            if (beatsPerMinute < _upperBounds[i])
            {
                return (HeartRateZone)(i + 1);
            }
        }

        return HeartRateZone.Zone5;
    }

    /// <summary>
    /// Seconds spent in each zone. Samples are weighted by the interval that
    /// follows them, so an irregular sampling rate does not silently bias the
    /// distribution toward whichever zone the device happened to log most often.
    /// </summary>
    public IReadOnlyDictionary<HeartRateZone, double> Distribution(
        IReadOnlyList<TrackPoint> points,
        TimeSpan maximumGap)
    {
        ArgumentNullException.ThrowIfNull(points);

        var seconds = new Dictionary<HeartRateZone, double>
        {
            [HeartRateZone.Zone1] = 0,
            [HeartRateZone.Zone2] = 0,
            [HeartRateZone.Zone3] = 0,
            [HeartRateZone.Zone4] = 0,
            [HeartRateZone.Zone5] = 0,
        };

        for (int i = 0; i < points.Count - 1; i++)
        {
            int? bpm = points[i].HeartRateBpm;
            if (bpm is not > 0)
            {
                continue;
            }

            TimeSpan interval = points[i + 1].Timestamp - points[i].Timestamp;
            if (interval <= TimeSpan.Zero || interval > maximumGap)
            {
                continue;
            }

            seconds[ZoneFor(bpm.Value)] += interval.TotalSeconds;
        }

        return seconds;
    }
}
