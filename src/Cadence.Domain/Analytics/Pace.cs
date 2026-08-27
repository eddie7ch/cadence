using System.Globalization;

namespace Cadence.Domain.Analytics;

/// <summary>
/// Seconds per kilometre. A primitive <c>double</c> for pace invites unit bugs -
/// min/km, min/mile, and m/s all look identical at a call site - so pace is a
/// type, and every conversion goes through it.
/// </summary>
public readonly record struct Pace : IComparable<Pace>
{
    public const double MetersPerKilometer = 1000.0;
    public const double MetersPerMile = 1609.344;

    private Pace(double secondsPerKilometer) => SecondsPerKilometer = secondsPerKilometer;

    public double SecondsPerKilometer { get; }

    public double SecondsPerMile => SecondsPerKilometer * (MetersPerMile / MetersPerKilometer);

    public double MetersPerSecond =>
        SecondsPerKilometer <= 0 ? 0 : MetersPerKilometer / SecondsPerKilometer;

    public double KilometersPerHour => MetersPerSecond * 3.6;

    public static Pace Zero => new(0);

    public static Pace FromSecondsPerKilometer(double seconds) =>
        new(double.IsFinite(seconds) && seconds > 0 ? seconds : 0);

    public static Pace FromSpeed(double metersPerSecond) =>
        metersPerSecond > 1e-6
            ? new(MetersPerKilometer / metersPerSecond)
            : Zero;

    public static Pace FromDistanceAndDuration(double meters, TimeSpan duration) =>
        meters > 1e-6 && duration > TimeSpan.Zero
            ? new(duration.TotalSeconds / (meters / MetersPerKilometer))
            : Zero;

    public int CompareTo(Pace other) => SecondsPerKilometer.CompareTo(other.SecondsPerKilometer);

    /// <summary>Formats as <c>m:ss/km</c>, the form every runner reads without thinking.</summary>
    public override string ToString()
    {
        if (SecondsPerKilometer <= 0)
        {
            return "-:--/km";
        }

        int total = (int)Math.Round(SecondsPerKilometer);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{total / 60}:{total % 60:D2}/km");
    }
}
