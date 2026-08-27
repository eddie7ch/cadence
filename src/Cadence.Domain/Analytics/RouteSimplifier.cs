using Cadence.Domain.Geo;

namespace Cadence.Domain.Analytics;

/// <summary>
/// Ramer-Douglas-Peucker line simplification.
///
/// A two-hour ride sampled once per second is 7,200 coordinates. Handing that
/// to a browser map is several megabytes of JSON and a visibly janky pan, for a
/// polyline whose extra vertices are all sub-pixel at any sensible zoom. This
/// reduces the point count by an order of magnitude while keeping every vertex
/// within <c>toleranceMeters</c> of the original path, so the drawn shape is
/// unchanged to the eye.
///
/// The recursion is written as an explicit stack: a pathological track can be
/// deep enough to blow the call stack, and an ingestion worker should not die
/// because someone uploaded a very long ride.
/// </summary>
public static class RouteSimplifier
{
    public const double DefaultToleranceMeters = 5.0;

    public static IReadOnlyList<T> Simplify<T>(
        IReadOnlyList<T> points,
        Func<T, (double Lat, double Lon)> selector,
        double toleranceMeters = DefaultToleranceMeters)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(selector);

        if (points.Count <= 2 || toleranceMeters <= 0)
        {
            return points;
        }

        var keep = new bool[points.Count];
        keep[0] = true;
        keep[^1] = true;

        var pending = new Stack<(int Start, int End)>();
        pending.Push((0, points.Count - 1));

        while (pending.Count > 0)
        {
            (int start, int end) = pending.Pop();
            if (end - start < 2)
            {
                continue;
            }

            (double Lat, double Lon) a = selector(points[start]);
            (double Lat, double Lon) b = selector(points[end]);

            double worst = -1;
            int worstIndex = -1;

            for (int i = start + 1; i < end; i++)
            {
                double distance = GeoMath.PerpendicularDistanceMeters(selector(points[i]), a, b);
                if (distance > worst)
                {
                    worst = distance;
                    worstIndex = i;
                }
            }

            if (worst > toleranceMeters && worstIndex > 0)
            {
                keep[worstIndex] = true;
                pending.Push((start, worstIndex));
                pending.Push((worstIndex, end));
            }
        }

        var simplified = new List<T>(points.Count / 4);
        for (int i = 0; i < points.Count; i++)
        {
            if (keep[i])
            {
                simplified.Add(points[i]);
            }
        }

        return simplified;
    }
}
