namespace Cadence.Application.Contracts;

/// <summary>
/// The wire contract. These types are what the API serialises; domain entities
/// never leave the Application layer, so a change to an entity cannot silently
/// become a breaking API change.
/// </summary>
public sealed record AthleteDto(
    Guid Id,
    string Email,
    string DisplayName,
    int? MaxHeartRate,
    int? RestingHeartRate,
    DateTimeOffset CreatedAt);

public sealed record AuthResponseDto(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    AthleteDto Athlete);

public sealed record ActivitySummaryDto(
    Guid Id,
    string Name,
    string Sport,
    string Status,
    DateTimeOffset StartedAt,
    double DistanceMeters,
    double MovingSeconds,
    double ElapsedSeconds,
    double ElevationGainMeters,
    double PaceSecondsPerKm,
    double GradeAdjustedPaceSecondsPerKm,
    int? AverageHeartRateBpm,
    string? Error);

public sealed record SplitDto(
    int Number,
    double DistanceMeters,
    double DurationSeconds,
    double PaceSecondsPerKm,
    double GradeAdjustedPaceSecondsPerKm,
    double ElevationGainMeters,
    int? AverageHeartRateBpm,
    bool IsComplete);

/// <summary>
/// GeoJSON-style coordinate pairs, ordered [longitude, latitude] to match RFC
/// 7946 - the order every mapping library expects and the one most often got
/// backwards.
/// </summary>
public sealed record RouteDto(
    IReadOnlyList<double[]> Coordinates,
    double[] BoundingBox,
    int PointCount,
    int SimplifiedPointCount);

public sealed record ActivityDetailDto(
    ActivitySummaryDto Summary,
    RouteDto? Route,
    IReadOnlyList<SplitDto> Splits,
    IReadOnlyDictionary<string, double> HeartRateZoneSeconds,
    int SampleCount,
    int DiscardedSampleCount);

/// <summary>
/// Column-oriented time series: one array per channel rather than an array of
/// objects. For 5,000 samples this is several times smaller on the wire and is
/// the shape charting libraries want anyway.
/// </summary>
public sealed record TimeSeriesDto(
    IReadOnlyList<double> ElapsedSeconds,
    IReadOnlyList<double> DistanceMeters,
    IReadOnlyList<double?> AltitudeMeters,
    IReadOnlyList<int?> HeartRateBpm,
    IReadOnlyList<double?> SpeedMetersPerSecond,
    IReadOnlyList<int?> CadenceRpm,
    IReadOnlyList<int?> PowerWatts,
    int Resolution);

public sealed record WeeklyTotalsDto(
    DateOnly WeekStart,
    int ActivityCount,
    double DistanceMeters,
    double ElevationGainMeters,
    double MovingSeconds,
    double? AverageHeartRateBpm);

public sealed record TrendsDto(
    IReadOnlyList<WeeklyTotalsDto> Weeks,
    double TotalDistanceMeters,
    double TotalElevationGainMeters,
    double TotalMovingSeconds,
    int TotalActivities,
    /// <summary>Percentage change in distance between the two halves of the window.</summary>
    double DistanceTrendPercent);

public sealed record CoachingFindingDto(string Title, string Detail, string Metric, string Severity);

public sealed record CoachingReportDto(
    Guid Id,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Summary,
    string Verdict,
    IReadOnlyList<CoachingFindingDto> Findings,
    int ActivityCount,
    string ModelId,
    DateTimeOffset GeneratedAt);

public sealed record NearbyActivityDto(
    Guid Id,
    string Name,
    string Sport,
    DateTimeOffset StartedAt,
    double DistanceMeters,
    double PaceSecondsPerKm);

public sealed record PagedDto<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages);
