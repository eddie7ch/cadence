using Cadence.Application.Abstractions;
using Cadence.Application.Common;
using Cadence.Application.Contracts;
using Cadence.Application.Mapping;
using Cadence.Domain.Activities;

namespace Cadence.Application.Handlers;

public sealed class ListActivitiesHandler
{
    /// <summary>
    /// An unbounded page size turns one request into a full table scan, so the
    /// ceiling is enforced here rather than trusted from the query string.
    /// </summary>
    public const int MaximumPageSize = 100;

    private readonly IActivityRepository _activities;

    public ListActivitiesHandler(IActivityRepository activities)
    {
        ArgumentNullException.ThrowIfNull(activities);
        _activities = activities;
    }

    public async Task<Result<PagedDto<ActivitySummaryDto>>> ExecuteAsync(
        Guid athleteId,
        ActivityQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Page < 1)
        {
            return Error.Validation("Page must be 1 or greater.");
        }

        if (query.PageSize is < 1 or > MaximumPageSize)
        {
            return Error.Validation($"Page size must be between 1 and {MaximumPageSize}.");
        }

        if (query.From is { } from && query.To is { } to && from > to)
        {
            return Error.Validation("The start of the range must not be after its end.");
        }

        if (query.MinimumDistanceMeters is { } minimum && (minimum < 0 || !double.IsFinite(minimum)))
        {
            return Error.Validation("Minimum distance must be a non-negative number of metres.");
        }

        PagedResult<Activity> page = await _activities.ListAsync(athleteId, query, cancellationToken);
        return page.ToDto(static activity => activity.ToSummaryDto());
    }
}
