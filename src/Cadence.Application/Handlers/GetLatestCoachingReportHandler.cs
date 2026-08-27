using Cadence.Application.Abstractions;
using Cadence.Application.Common;
using Cadence.Application.Contracts;
using Cadence.Application.Mapping;
using Cadence.Domain.Coaching;

namespace Cadence.Application.Handlers;

/// <summary>
/// Returns the most recent stored report without generating a new one. Kept
/// separate from <see cref="GenerateCoachingReportHandler"/> because generating
/// costs a model call, and a page load must never trigger one by accident.
/// </summary>
public sealed class GetLatestCoachingReportHandler
{
    private readonly ICoachingReportRepository _reports;

    public GetLatestCoachingReportHandler(ICoachingReportRepository reports)
    {
        ArgumentNullException.ThrowIfNull(reports);
        _reports = reports;
    }

    public async Task<Result<CoachingReportDto>> ExecuteAsync(
        Guid athleteId,
        CancellationToken cancellationToken = default)
    {
        CoachingReport? report = await _reports.FindLatestAsync(athleteId, cancellationToken);

        return report is null
            ? Error.NotFound("No coaching report has been generated yet.")
            : report.ToDto();
    }
}
