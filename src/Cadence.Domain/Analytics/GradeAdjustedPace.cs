namespace Cadence.Domain.Analytics;

/// <summary>
/// Grade-adjusted pace: the flat-ground pace that would cost the same energy as
/// the pace actually run on a slope.
///
/// Uses the metabolic cost-of-running polynomial from Minetti et al. (2002),
/// "Energy cost of walking and running at extreme uphill and downhill slopes",
/// J. Appl. Physiol. 93:1039-1046, which fits treadmill measurements over
/// gradients of -45% to +45%:
///
///     Cr(i) = 155.4i^5 - 30.4i^4 - 43.3i^3 + 46.3i^2 + 19.5i + 3.6   [J/kg/m]
///
/// The adjustment factor is Cr(i)/Cr(0). Note it is not monotonic: a shallow
/// descent is *cheaper* than flat (the minimum sits near -10% grade), while a
/// steep descent costs more again because of braking. A naive "downhill is
/// always easier" adjustment gets that backwards.
/// </summary>
public static class GradeAdjustedPace
{
    /// <summary>Cost of running on the flat, J/kg/m - the constant term of the polynomial.</summary>
    public const double FlatCostJoulesPerKgPerMeter = 3.6;

    /// <summary>The polynomial is only fitted between -45% and +45%; beyond that it diverges.</summary>
    public const double MaxAbsoluteGradient = 0.45;

    /// <summary>Metabolic cost of running at gradient <paramref name="gradient"/> (rise/run).</summary>
    public static double CostOfRunning(double gradient)
    {
        double i = Math.Clamp(gradient, -MaxAbsoluteGradient, MaxAbsoluteGradient);
        double i2 = i * i;
        double i3 = i2 * i;
        double i4 = i3 * i;
        double i5 = i4 * i;

        return (155.4 * i5)
            - (30.4 * i4)
            - (43.3 * i3)
            + (46.3 * i2)
            + (19.5 * i)
            + FlatCostJoulesPerKgPerMeter;
    }

    /// <summary>
    /// Multiplier converting actual speed to equivalent flat speed. Greater than
    /// one uphill (you are working harder than your watch pace suggests) and
    /// less than one on a gentle descent.
    /// </summary>
    public static double AdjustmentFactor(double gradient) =>
        CostOfRunning(gradient) / FlatCostJoulesPerKgPerMeter;

    public static Pace Adjust(Pace actual, double gradient)
    {
        if (actual.SecondsPerKilometer <= 0)
        {
            return Pace.Zero;
        }

        return Pace.FromSpeed(actual.MetersPerSecond * AdjustmentFactor(gradient));
    }

    /// <summary>
    /// Distance-weighted grade-adjusted pace over a series of segments.
    /// Weighting by distance rather than by time is what makes a short brutal
    /// climb count for its length and not for how long it took to survive.
    /// </summary>
    public static Pace OverSegments(IEnumerable<(double DistanceMeters, double RiseMeters, double Seconds)> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        double equivalentDistance = 0;
        double totalSeconds = 0;

        foreach ((double distance, double rise, double seconds) in segments)
        {
            if (distance <= 0 || seconds <= 0)
            {
                continue;
            }

            double gradient = rise / distance;
            equivalentDistance += distance * AdjustmentFactor(gradient);
            totalSeconds += seconds;
        }

        return Pace.FromDistanceAndDuration(equivalentDistance, TimeSpan.FromSeconds(totalSeconds));
    }
}
