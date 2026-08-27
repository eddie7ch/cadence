using Cadence.Application.Abstractions;
using Cadence.Domain.Activities;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Cadence.Infrastructure.Persistence.Repositories;

internal sealed class ActivityRepository : IActivityRepository
{
    private const int Wgs84 = 4326;
    private const int MaxPageSize = 200;

    private readonly CadenceDbContext _context;

    public ActivityRepository(CadenceDbContext context) => _context = context;

    public Task<Activity?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.Activities.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<Activity?> FindWithSplitsAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.Activities
            .Include(a => a.Splits.OrderBy(s => s.Number))
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ActivitySample>> GetSamplesAsync(
        Guid activityId,
        CancellationToken cancellationToken = default) =>
        await _context.ActivitySamples
            .AsNoTracking()
            .Where(s => s.ActivityId == activityId)
            .OrderBy(s => s.Sequence)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<Activity?> FindByChecksumAsync(
        Guid athleteId,
        string checksum,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checksum);

        return _context.Activities
            .FirstOrDefaultAsync(
                a => a.AthleteId == athleteId && a.SourceChecksum == checksum,
                cancellationToken);
    }

    public async Task<PagedResult<Activity>> ListAsync(
        Guid athleteId,
        ActivityQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        var filtered = _context.Activities
            .AsNoTracking()
            .Where(a => a.AthleteId == athleteId);

        if (query.Sport is { } sport)
        {
            filtered = filtered.Where(a => a.Sport == sport);
        }

        if (query.From is { } from)
        {
            filtered = filtered.Where(a => a.StartedAt >= from);
        }

        if (query.To is { } to)
        {
            filtered = filtered.Where(a => a.StartedAt <= to);
        }

        if (query.MinimumDistanceMeters is { } minimumDistance)
        {
            filtered = filtered.Where(a => a.DistanceMeters >= minimumDistance);
        }

        var totalCount = await filtered.CountAsync(cancellationToken).ConfigureAwait(false);

        // Id is a v7 GUID, so it breaks StartedAt ties in insertion order and keeps paging
        // stable when several files share a start timestamp.
        var items = await filtered
            .OrderByDescending(a => a.StartedAt)
            .ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<Activity>(items, page, pageSize, totalCount);
    }

    public async Task<IReadOnlyList<Activity>> FindNearAsync(
        Guid athleteId,
        double latitude,
        double longitude,
        double radiusMeters,
        int limit,
        CancellationToken cancellationToken = default)
    {
        // NTS is x/y ordered, so longitude comes first.
        var origin = new Point(longitude, latitude) { SRID = Wgs84 };

        // The useSpheroid overload is the point of this call: it makes Npgsql emit
        // ST_DWithin(route::geography, origin::geography, radius, true). Without the
        // geography cast ST_DWithin measures in SRID units - degrees for 4326 - and
        // "within 500" would mean 500 degrees, which is the whole planet.
        return await _context.Activities
            .AsNoTracking()
            .Where(a => a.AthleteId == athleteId
                && a.Route != null
                && EF.Functions.IsWithinDistance(a.Route!, origin, radiusMeters, useSpheroid: true))
            .OrderByDescending(a => a.StartedAt)
            .Take(Math.Max(1, limit))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WeeklyTotals>> GetWeeklyTotalsAsync(
        Guid athleteId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        // Raw SQL rather than LINQ, deliberately. The aggregate needs
        // date_trunc as a grouping key alongside SUM(EXTRACT(EPOCH FROM interval))
        // over the moving-time column, and EF cannot translate that combination -
        // it fails at runtime with a client-evaluation error rather than at build.
        // Falling back to grouping in memory would pull every activity in the
        // window across the wire to produce a dozen rows, which is precisely what
        // this method exists to avoid. The query is fully parameterised.
        var rows = await _context.Database
            .SqlQuery<WeeklyRow>(
                $"""
                 SELECT (date_trunc('week', a."StartedAt" AT TIME ZONE 'UTC'))::date AS "WeekStart",
                        COUNT(*)::int                                                AS "ActivityCount",
                        COALESCE(SUM(a."DistanceMeters"), 0)                         AS "DistanceMeters",
                        COALESCE(SUM(a."ElevationGainMeters"), 0)                    AS "ElevationGainMeters",
                        COALESCE(SUM(EXTRACT(EPOCH FROM a."MovingTime")), 0)::double precision AS "MovingSeconds",
                        AVG(a."AverageHeartRateBpm")::double precision               AS "AverageHeartRateBpm"
                 FROM "Activities" a
                 WHERE a."AthleteId" = {athleteId}
                   AND a."Status" = 'Ready'
                   AND a."StartedAt" >= {from}
                   AND a."StartedAt" <= {to}
                 GROUP BY 1
                 ORDER BY 1
                 """)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(row => new WeeklyTotals(
                row.WeekStart,
                row.ActivityCount,
                row.DistanceMeters,
                row.ElevationGainMeters,
                row.MovingSeconds,
                row.AverageHeartRateBpm))
            .ToList();
    }

    public void Add(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        _context.Activities.Add(activity);
    }

    public void Remove(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        _context.Activities.Remove(activity);
    }

    private sealed record WeeklyRow(
        DateOnly WeekStart,
        int ActivityCount,
        double DistanceMeters,
        double ElevationGainMeters,
        double MovingSeconds,
        double? AverageHeartRateBpm);
}
