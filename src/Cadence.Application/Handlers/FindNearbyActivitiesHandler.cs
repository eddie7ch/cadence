using System.Globalization;
using Cadence.Application.Abstractions;
using Cadence.Application.Common;
using Cadence.Application.Contracts;
using Cadence.Application.Mapping;
using Cadence.Domain.Activities;

namespace Cadence.Application.Handlers;

public sealed class FindNearbyActivitiesHandler
{
    public const int DefaultLimit = 20;
    public const int MaximumLimit = 100;

    /// <summary>
    /// Beyond this the query stops being "activities near here" and becomes a
    /// scan of the athlete's whole history with a geometry filter attached.
    /// </summary>
    public const double MaximumRadiusMeters = 50_000;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly IActivityRepository _activities;
    private readonly IAnalyticsCache _cache;

    public FindNearbyActivitiesHandler(IActivityRepository activities, IAnalyticsCache cache)
    {
        ArgumentNullException.ThrowIfNull(activities);
        ArgumentNullException.ThrowIfNull(cache);

        _activities = activities;
        _cache = cache;
    }

    public async Task<Result<IReadOnlyList<NearbyActivityDto>>> ExecuteAsync(
        Guid athleteId,
        double latitude,
        double longitude,
        double radiusMeters,
        int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        // NaN fails every comparison, so finiteness is checked before the range.
        if (!double.IsFinite(latitude) || latitude is < -90 or > 90)
        {
            return Error.Validation("Latitude must be between -90 and 90.");
        }

        if (!double.IsFinite(longitude) || longitude is < -180 or > 180)
        {
            return Error.Validation("Longitude must be between -180 and 180.");
        }

        if (!double.IsFinite(radiusMeters) || radiusMeters <= 0 || radiusMeters > MaximumRadiusMeters)
        {
            return Error.Validation($"Radius must be greater than 0 and at most {MaximumRadiusMeters} metres.");
        }

        if (limit < 1)
        {
            return Error.Validation("Limit must be at least 1.");
        }

        int cappedLimit = Math.Min(limit, MaximumLimit);

        // Five decimal places is roughly a metre. Rounding the centre into the
        // key lets a map that jitters by a pixel between pans reuse the entry
        // instead of missing the cache on every frame.
        string key = string.Create(
            CultureInfo.InvariantCulture,
            $"nearby:{latitude:F5}:{longitude:F5}:{radiusMeters:F0}:{cappedLimit}");

        List<NearbyActivityDto> nearby = await _cache.GetOrCreateAsync(
            athleteId,
            key,
            CacheTtl,
            token => FindAsync(athleteId, latitude, longitude, radiusMeters, cappedLimit, token),
            cancellationToken);

        return Result<IReadOnlyList<NearbyActivityDto>>.Success(nearby);
    }

    private async Task<List<NearbyActivityDto>> FindAsync(
        Guid athleteId,
        double latitude,
        double longitude,
        double radiusMeters,
        int limit,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Activity> matches = await _activities.FindNearAsync(
            athleteId,
            latitude,
            longitude,
            radiusMeters,
            limit,
            cancellationToken);

        return [.. matches.Select(activity => activity.ToNearbyDto())];
    }
}
