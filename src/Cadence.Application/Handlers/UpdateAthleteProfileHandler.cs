using Cadence.Application.Abstractions;
using Cadence.Application.Common;
using Cadence.Application.Contracts;
using Cadence.Application.Mapping;
using Cadence.Domain.Athletes;

namespace Cadence.Application.Handlers;

/// <summary>
/// Sets the physiological reference values the analytics depend on.
///
/// Heart-rate zones are meaningless without a maximum, and there is no honest way
/// to infer one from the data: the highest rate in a given activity is whatever
/// that session happened to reach, so deriving zone boundaries from it guarantees
/// the session looks maximal. Either the athlete supplies a measured maximum or
/// the API declines to report zones.
/// </summary>
public sealed class UpdateAthleteProfileHandler
{
    private readonly IAthleteRepository _athletes;
    private readonly IAnalyticsCache _cache;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateAthleteProfileHandler(
        IAthleteRepository athletes,
        IAnalyticsCache cache,
        IUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(athletes);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(unitOfWork);

        _athletes = athletes;
        _cache = cache;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<AthleteDto>> ExecuteAsync(
        Guid athleteId,
        int? maxHeartRate,
        int? restingHeartRate,
        int? birthYear,
        double? weightKilograms,
        CancellationToken cancellationToken = default)
    {
        Athlete? athlete = await _athletes.FindByIdAsync(athleteId, cancellationToken);
        if (athlete is null)
        {
            return Error.NotFound("Athlete not found.");
        }

        if (birthYear is { } year && (year < 1900 || year > DateTime.UtcNow.Year))
        {
            return Error.Validation("Birth year is outside a plausible range.");
        }

        if (weightKilograms is <= 0 or > 400)
        {
            return Error.Validation("Weight must be between 0 and 400 kg.");
        }

        try
        {
            athlete.UpdateProfile(maxHeartRate, restingHeartRate, birthYear, weightKilograms);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or ArgumentException)
        {
            // The entity guards its own invariants; the handler's job is to turn a
            // violated invariant into a 400 rather than letting it become a 500.
            return Error.Validation(ex.Message);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Zone distributions are cached per activity and are all now computed
        // against the wrong boundaries.
        await _cache.InvalidateAthleteAsync(athleteId, cancellationToken);

        return athlete.ToDto();
    }
}
