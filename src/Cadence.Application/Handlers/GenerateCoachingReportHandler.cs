using Cadence.Application.Abstractions;
using Cadence.Application.Common;
using Cadence.Application.Contracts;
using Cadence.Application.Mapping;
using Cadence.Domain.Activities;
using Cadence.Domain.Analytics;
using Cadence.Domain.Athletes;
using Cadence.Domain.Coaching;
using Microsoft.Extensions.Logging;

namespace Cadence.Application.Handlers;

public sealed class GenerateCoachingReportHandler
{
    public const int DefaultWeeks = 4;
    public const int MaximumWeeks = 26;

    /// <summary>
    /// The advisor is given pre-aggregated sessions, not a training diary. Past
    /// a few dozen the prompt grows without the assessment improving.
    /// </summary>
    public const int MaximumRecentActivities = 25;

    private readonly IActivityRepository _activities;
    private readonly IAthleteRepository _athletes;
    private readonly ICoachingReportRepository _reports;
    private readonly ICoachingAdvisor _advisor;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly ILogger<GenerateCoachingReportHandler> _logger;

    public GenerateCoachingReportHandler(
        IActivityRepository activities,
        IAthleteRepository athletes,
        ICoachingReportRepository reports,
        ICoachingAdvisor advisor,
        IUnitOfWork unitOfWork,
        IClock clock,
        ILogger<GenerateCoachingReportHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(activities);
        ArgumentNullException.ThrowIfNull(athletes);
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(advisor);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _activities = activities;
        _athletes = athletes;
        _reports = reports;
        _advisor = advisor;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _logger = logger;
    }

    /// <param name="refresh">
    /// Forces a new analysis even when a report already covers this window. Each
    /// run is a paid model call, so the default is to hand back the existing one.
    /// </param>
    public async Task<Result<CoachingReportDto>> ExecuteAsync(
        Guid athleteId,
        int weeks = DefaultWeeks,
        bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        if (weeks is < 1 or > MaximumWeeks)
        {
            return Error.Validation($"The review window must be between 1 and {MaximumWeeks} weeks.");
        }

        // Reported as unavailable rather than thrown: a deployment without an
        // Anthropic key is a supported configuration, not a broken one.
        if (!_advisor.IsConfigured)
        {
            return Error.Unavailable("Coaching analysis is not configured on this deployment.");
        }

        Athlete? athlete = await _athletes.FindByIdAsync(athleteId, cancellationToken);
        if (athlete is null)
        {
            return Error.NotFound("Athlete not found.");
        }

        DateTimeOffset end = _clock.UtcNow;
        DateTimeOffset start = end.AddDays(-7 * weeks);
        var periodStart = DateOnly.FromDateTime(start.UtcDateTime);
        var periodEnd = DateOnly.FromDateTime(end.UtcDateTime);

        if (!refresh)
        {
            CoachingReport? latest = await _reports.FindLatestAsync(athleteId, cancellationToken);
            if (latest is not null && latest.PeriodStart == periodStart && latest.PeriodEnd == periodEnd)
            {
                return latest.ToDto();
            }
        }

        IReadOnlyList<WeeklyTotals> totals =
            await _activities.GetWeeklyTotalsAsync(athleteId, start, end, cancellationToken);

        PagedResult<Activity> recent = await _activities.ListAsync(
            athleteId,
            new ActivityQuery { From = start, To = end, Page = 1, PageSize = MaximumRecentActivities },
            cancellationToken);

        List<CoachingActivitySummary> sessions =
        [
            .. recent.Items
                .Where(activity => activity.Status is ActivityStatus.Ready)
                .Select(ToSummary),
        ];

        if (sessions.Count == 0)
        {
            return Error.Unprocessable("There are no processed activities in this window to analyse.");
        }

        var input = new CoachingInput(periodStart, periodEnd, totals, sessions, athlete.MaxHeartRate);

        CoachingAnalysis analysis;
        try
        {
            analysis = await _advisor.AnalyzeAsync(input, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A model outage is an operational condition the caller can retry,
            // not a defect in this request.
            _logger.LogError(ex, "Coaching analysis failed for athlete {AthleteId}.", athleteId);
            return Error.Unavailable("The coaching advisor could not complete the analysis.");
        }

        if (string.IsNullOrWhiteSpace(analysis.Summary) || string.IsNullOrWhiteSpace(analysis.ModelId))
        {
            _logger.LogError("The coaching advisor returned an analysis with no summary or model id.");
            return Error.Unavailable("The coaching advisor returned an unusable analysis.");
        }

        CoachingReport report = CoachingReport.Create(
            athleteId,
            periodStart,
            periodEnd,
            analysis.Summary,
            analysis.Verdict,
            analysis.Findings,
            sessions.Count,
            analysis.ModelId,
            _clock.UtcNow);

        _reports.Add(report);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return report.ToDto();
    }

    private static CoachingActivitySummary ToSummary(Activity activity) => new(
        DateOnly.FromDateTime(activity.StartedAt.UtcDateTime),
        activity.Sport.ToString(),
        activity.DistanceMeters / Pace.MetersPerKilometer,
        activity.MovingTime.TotalMinutes,
        activity.AveragePaceSecondsPerKm,
        activity.GradeAdjustedPaceSecondsPerKm,
        activity.ElevationGainMeters,
        activity.AverageHeartRateBpm);
}
