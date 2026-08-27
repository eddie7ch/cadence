using Cadence.Application.Abstractions;
using Cadence.Application.Common;
using Cadence.Domain.Activities;

namespace Cadence.Application.Handlers;

public sealed class DeleteActivityHandler
{
    private readonly IActivityRepository _activities;
    private readonly IActivityFileStore _files;
    private readonly IAnalyticsCache _cache;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteActivityHandler(
        IActivityRepository activities,
        IActivityFileStore files,
        IAnalyticsCache cache,
        IUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(activities);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(unitOfWork);

        _activities = activities;
        _files = files;
        _cache = cache;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> ExecuteAsync(
        Guid activityId,
        Guid athleteId,
        CancellationToken cancellationToken = default)
    {
        Activity? activity = await _activities.FindByIdAsync(activityId, cancellationToken);

        // Someone else's activity is reported as absent rather than forbidden, so
        // the API never confirms that an id belongs to another athlete.
        if (activity is null || activity.AthleteId != athleteId)
        {
            return Error.NotFound("Activity not found.");
        }

        _activities.Remove(activity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Row first, bytes second. Deleting the file before the row commits would
        // leave an activity that can never be reprocessed if the delete rolls back.
        await _files.DeleteAsync(
            activity.AthleteId,
            activity.SourceChecksum,
            activity.SourceFileName,
            cancellationToken);

        await _cache.InvalidateAthleteAsync(athleteId, cancellationToken);

        return Result.Success();
    }
}
