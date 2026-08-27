namespace Cadence.Domain.Activities;

/// <summary>
/// A completed distance interval. Persisted rather than recomputed because
/// splits are what a training log is read for, and recomputing them means
/// loading every sample of the activity.
/// </summary>
public sealed class ActivitySplit
{
    private ActivitySplit()
    {
    }

    public ActivitySplit(
        Guid activityId,
        int number,
        double distanceMeters,
        TimeSpan duration,
        double paceSecondsPerKm,
        double gradeAdjustedPaceSecondsPerKm,
        double elevationGainMeters,
        int? averageHeartRateBpm,
        bool isComplete)
    {
        ActivityId = activityId;
        Number = number;
        DistanceMeters = distanceMeters;
        Duration = duration;
        PaceSecondsPerKm = paceSecondsPerKm;
        GradeAdjustedPaceSecondsPerKm = gradeAdjustedPaceSecondsPerKm;
        ElevationGainMeters = elevationGainMeters;
        AverageHeartRateBpm = averageHeartRateBpm;
        IsComplete = isComplete;
    }

    public Guid ActivityId { get; private set; }

    /// <summary>One-based split index.</summary>
    public int Number { get; private set; }

    public double DistanceMeters { get; private set; }

    public TimeSpan Duration { get; private set; }

    public double PaceSecondsPerKm { get; private set; }

    public double GradeAdjustedPaceSecondsPerKm { get; private set; }

    public double ElevationGainMeters { get; private set; }

    public int? AverageHeartRateBpm { get; private set; }

    /// <summary>False for a trailing partial split, which must not be ranked against full ones.</summary>
    public bool IsComplete { get; private set; }
}
