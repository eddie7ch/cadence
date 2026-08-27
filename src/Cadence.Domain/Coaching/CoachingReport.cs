namespace Cadence.Domain.Coaching;

/// <summary>How hard the block was, judged against the athlete's own recent history.</summary>
public enum TrainingLoadVerdict
{
    Unknown = 0,
    Detraining = 1,
    Maintaining = 2,
    Productive = 3,
    Overreaching = 4,
}

/// <summary>
/// A structured observation. The model is constrained to this shape by a JSON
/// schema rather than asked for prose, so the output can be stored in columns,
/// filtered, and charted - and so a malformed response is a validation failure
/// at the boundary instead of a parsing surprise three layers in.
/// </summary>
public sealed record CoachingFinding(
    string Title,
    string Detail,
    string Metric,
    string Severity);

public sealed class CoachingReport
{
    private readonly List<CoachingFinding> _findings = [];

    private CoachingReport()
    {
        Summary = null!;
        ModelId = null!;
    }

    private CoachingReport(
        Guid id,
        Guid athleteId,
        DateOnly periodStart,
        DateOnly periodEnd,
        string summary,
        TrainingLoadVerdict verdict,
        string modelId,
        DateTimeOffset generatedAt)
    {
        Id = id;
        AthleteId = athleteId;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        Summary = summary;
        Verdict = verdict;
        ModelId = modelId;
        GeneratedAt = generatedAt;
    }

    public Guid Id { get; private set; }

    public Guid AthleteId { get; private set; }

    public DateOnly PeriodStart { get; private set; }

    public DateOnly PeriodEnd { get; private set; }

    public string Summary { get; private set; }

    public TrainingLoadVerdict Verdict { get; private set; }

    /// <summary>Which model produced this, so an old report is never mistaken for a current one.</summary>
    public string ModelId { get; private set; }

    public DateTimeOffset GeneratedAt { get; private set; }

    /// <summary>Activity count the report was derived from; a verdict from two runs is not a verdict.</summary>
    public int ActivityCount { get; private set; }

    public IReadOnlyCollection<CoachingFinding> Findings => _findings;

    public static CoachingReport Create(
        Guid athleteId,
        DateOnly periodStart,
        DateOnly periodEnd,
        string summary,
        TrainingLoadVerdict verdict,
        IEnumerable<CoachingFinding> findings,
        int activityCount,
        string modelId,
        DateTimeOffset generatedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentNullException.ThrowIfNull(findings);

        var report = new CoachingReport(
            Guid.CreateVersion7(),
            athleteId,
            periodStart,
            periodEnd,
            summary,
            verdict,
            modelId,
            generatedAt)
        {
            ActivityCount = activityCount,
        };

        report._findings.AddRange(findings);
        return report;
    }
}
