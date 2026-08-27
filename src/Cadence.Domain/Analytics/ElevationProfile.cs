namespace Cadence.Domain.Analytics;

/// <summary>
/// Total ascent and descent from a noisy altitude series.
///
/// This is the single most commonly wrong number in fitness software. Summing
/// every positive delta of a barometric or GPS altitude stream accumulates the
/// sensor noise as well as the hills: a flat 10 km run sampled once per second
/// with +/-1 m of jitter reports several hundred metres of climb.
///
/// The fix is two stages. First smooth the series to kill high-frequency noise,
/// then apply a hysteresis (ratchet) filter that only books a change once the
/// altitude has moved past a threshold from the last committed reference. The
/// thresholds below are in the range used by mainstream platforms.
/// </summary>
public static class ElevationProfile
{
    public const double DefaultThresholdMeters = 3.0;
    public const int DefaultSmoothingWindow = 5;

    public readonly record struct Result(double GainMeters, double LossMeters, double[] Smoothed);

    public static Result Compute(
        IReadOnlyList<double?> altitudes,
        double thresholdMeters = DefaultThresholdMeters,
        int smoothingWindow = DefaultSmoothingWindow)
    {
        ArgumentNullException.ThrowIfNull(altitudes);

        double[] filled = ForwardFill(altitudes);
        if (filled.Length == 0)
        {
            return new Result(0, 0, []);
        }

        double[] smoothed = MovingAverage(filled, smoothingWindow);

        double gain = 0;
        double loss = 0;
        double reference = smoothed[0];

        foreach (double value in smoothed)
        {
            double delta = value - reference;
            if (delta >= thresholdMeters)
            {
                gain += delta;
                reference = value;
            }
            else if (delta <= -thresholdMeters)
            {
                loss += -delta;
                reference = value;
            }
        }

        return new Result(gain, loss, smoothed);
    }

    /// <summary>
    /// Replaces gaps with the last known altitude, and leading gaps with the
    /// first. A dropped sample is not a descent to sea level, and interpolating
    /// across it would invent a slope.
    ///
    /// The result is deliberately the same length as the input: callers index
    /// the smoothed series alongside their own sample list, and a shorter array
    /// would silently misalign every altitude with the wrong point.
    /// </summary>
    private static double[] ForwardFill(IReadOnlyList<double?> altitudes)
    {
        double? seed = null;
        foreach (double? altitude in altitudes)
        {
            if (altitude.HasValue && double.IsFinite(altitude.Value))
            {
                seed = altitude.Value;
                break;
            }
        }

        if (seed is null)
        {
            return [];
        }

        var result = new double[altitudes.Count];
        double last = seed.Value;

        for (int i = 0; i < altitudes.Count; i++)
        {
            double? altitude = altitudes[i];
            if (altitude.HasValue && double.IsFinite(altitude.Value))
            {
                last = altitude.Value;
            }

            result[i] = last;
        }

        return result;
    }

    /// <summary>Centred moving average; the window is clamped at both ends rather than padded.</summary>
    public static double[] MovingAverage(double[] values, int window)
    {
        if (values.Length == 0 || window <= 1)
        {
            return values;
        }

        int half = window / 2;
        var smoothed = new double[values.Length];

        for (int i = 0; i < values.Length; i++)
        {
            int start = Math.Max(0, i - half);
            int end = Math.Min(values.Length - 1, i + half);

            double sum = 0;
            for (int j = start; j <= end; j++)
            {
                sum += values[j];
            }

            smoothed[i] = sum / (end - start + 1);
        }

        return smoothed;
    }
}
