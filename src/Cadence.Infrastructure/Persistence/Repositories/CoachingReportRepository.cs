using Cadence.Application.Abstractions;
using Cadence.Domain.Coaching;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Infrastructure.Persistence.Repositories;

internal sealed class CoachingReportRepository : ICoachingReportRepository
{
    private readonly CadenceDbContext _context;

    public CoachingReportRepository(CadenceDbContext context) => _context = context;

    public Task<CoachingReport?> FindLatestAsync(Guid athleteId, CancellationToken cancellationToken = default) =>
        _context.CoachingReports
            .AsNoTracking()
            .Where(r => r.AthleteId == athleteId)
            .OrderByDescending(r => r.GeneratedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public void Add(CoachingReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        _context.CoachingReports.Add(report);
    }
}
