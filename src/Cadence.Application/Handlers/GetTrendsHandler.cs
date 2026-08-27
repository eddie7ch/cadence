using System.Globalization;
using Cadence.Application.Abstractions;
using Cadence.Application.Common;
using Cadence.Application.Contracts;
using Cadence.Application.Mapping;

namespace Cadence.Application.Handlers;

public sealed class GetTrendsHandler
{
    public const int DefaultWeeks = 12;

    /// <summary>
    /// Short enough that a freshly processed activity shows up promptly even if
    /// invalidation is missed, long enough that a dashboard refresh does not
    /// re-run the rollup.
    /// </summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    private readonly IActivityRepository _activities;
    private readonly IAnalyticsCache _cache;
    private readonly IClock _clock;

    public GetTrendsHandler(IActivityRepository activities, IAnalyticsCache cache, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(activities);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(clock);

        _activities = activities;
        _cache = cache;
        _clock = clock;
    }

    public async Task<Result<TrendsDto>> ExecuteAsync(
        Guid athleteId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset end = to ?? _clock.UtcNow;
        DateTimeOffset start = from ?? end.AddDays(-7 * DefaultWeeks);

        if (start >= end)
        {
            return Error.Validation("The start of the range must be before its end.");
        }

        // The window is keyed by date, not by instant: an unbounded default of
        // "now" would otherwise mint a new cache entry on every request.
        string key = string.Create(
            CultureInfo.InvariantCulture,
            $"athlete:{athleteId}:trends:{start.UtcDateTime:yyyyMMdd}:{end.UtcDateTime:yyyyMMdd}");

        return await _cache.GetOrCreateAsync(
            key,
            CacheTtl,
            token => BuildAsync(athleteId, start, end, token),
            cancellationToken);
    }

    private async Task<TrendsDto> BuildAsync(
        Guid athleteId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WeeklyTotals> totals =
            await _activities.GetWeeklyTotalsAsync(athleteId, from, to, cancellationToken);

        List<WeeklyTotalsDto> weeks = [.. totals.OrderBy(week => week.WeekStart).Select(week => week.ToDto())];

        double distance = 0;
        double elevation = 0;
        double moving = 0;
        int activities = 0;

        foreach (WeeklyTotalsDto week in weeks)
        {
            distance += week.DistanceMeters;
            elevation += week.ElevationGainMeters;
            moving += week.MovingSeconds;
            activities += week.ActivityCount;
        }

        return new TrendsDto(weeks, distance, elevation, moving, activities, DistanceTrendPercent(weeks));
    }

    /// <summary>
    /// Change in weekly distance between the older and newer halves of the
    /// window. With an odd number of weeks the middle one is excluded rather
    /// than counted on one side, which would bias the comparison by a whole week
    /// of training.
    /// </summary>
    private static double DistanceTrendPercent(IReadOnlyList<WeeklyTotalsDto> weeks)
    {
        int half = weeks.Count / 2;
        if (half == 0)
        {
            return 0;
        }

        double older = 0;
        for (int i = 0; i < half; i++)
        {
            older += weeks[i].DistanceMeters;
        }

        double newer = 0;
        for (int i = weeks.Count - half; i < weeks.Count; i++)
        {
            newer += weeks[i].DistanceMeters;
        }

        if (older <= 0)
        {
            // Coming back from nothing is not an infinite improvement; report it
            // as a full increase rather than dividing by zero.
            return newer > 0 ? 100 : 0;
        }

        return (newer - older) / older * 100.0;
    }
}
