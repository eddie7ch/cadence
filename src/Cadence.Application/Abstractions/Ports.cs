using Cadence.Domain.Activities;
using Cadence.Domain.Analytics;
using Cadence.Domain.Athletes;
using Cadence.Domain.Coaching;

namespace Cadence.Application.Abstractions;

/// <summary>Wall clock, injected so time-dependent behaviour is testable.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string hash);
}

public interface ITokenIssuer
{
    /// <summary>Returns the signed token and the number of seconds until it expires.</summary>
    (string Token, int ExpiresInSeconds) Issue(Athlete athlete);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IAthleteRepository
{
    Task<Athlete?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Athlete?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default);

    void Add(Athlete athlete);
}

/// <summary>Filter for a paged activity listing. All fields are optional.</summary>
public sealed record ActivityQuery
{
    public Sport? Sport { get; init; }

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    public double? MinimumDistanceMeters { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
}

public interface IActivityRepository
{
    /// <summary>Header only - no samples, no splits.</summary>
    Task<Activity?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Header plus splits, which is what the detail view needs.</summary>
    Task<Activity?> FindWithSplitsAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ActivitySample>> GetSamplesAsync(
        Guid activityId,
        CancellationToken cancellationToken = default);

    Task<Activity?> FindByChecksumAsync(
        Guid athleteId,
        string checksum,
        CancellationToken cancellationToken = default);

    Task<PagedResult<Activity>> ListAsync(
        Guid athleteId,
        ActivityQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Activities whose route passes within <paramref name="radiusMeters"/> of a
    /// point. Implemented with a PostGIS <c>ST_DWithin</c> against a GiST index,
    /// not by loading routes and measuring in C#.
    /// </summary>
    Task<IReadOnlyList<Activity>> FindNearAsync(
        Guid athleteId,
        double latitude,
        double longitude,
        double radiusMeters,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Weekly rollups over a date range, aggregated in SQL.</summary>
    Task<IReadOnlyList<WeeklyTotals>> GetWeeklyTotalsAsync(
        Guid athleteId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    void Add(Activity activity);

    void Remove(Activity activity);
}

public sealed record WeeklyTotals(
    DateOnly WeekStart,
    int ActivityCount,
    double DistanceMeters,
    double ElevationGainMeters,
    double MovingSeconds,
    double? AverageHeartRateBpm);

public interface ICoachingReportRepository
{
    Task<CoachingReport?> FindLatestAsync(Guid athleteId, CancellationToken cancellationToken = default);

    void Add(CoachingReport report);
}

/// <summary>
/// Read-through cache for analytics that are expensive to derive and cheap to
/// invalidate. Keys are namespaced per athlete so a single athlete's data can be
/// evicted without flushing everyone.
/// </summary>
public interface IAnalyticsCache
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        where T : class;

    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>
    /// Fetches or computes under a per-key lock, so a cold key hit by fifty
    /// concurrent requests runs the expensive query once rather than fifty times.
    /// </summary>
    Task<T> GetOrCreateAsync<T>(
        string key,
        TimeSpan ttl,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>Bumps the athlete's key version, invalidating every derived entry at once.</summary>
    Task InvalidateAthleteAsync(Guid athleteId, CancellationToken cancellationToken = default);
}

/// <summary>What a device-file parser produces before analysis.</summary>
public sealed record ParsedActivity(
    IReadOnlyList<TrackPoint> Points,
    Sport Sport,
    string? Name,
    SourceFormat Format,
    string? DeviceName);

/// <summary>
/// Where uploaded device files live.
///
/// Content-addressed by checksum, so the same bytes are stored once and a
/// re-upload costs nothing. The store owns path construction entirely: no caller
/// builds a path, which is what lets the background worker reopen a file knowing
/// only the activity it belongs to.
/// </summary>
public interface IActivityFileStore
{
    Task SaveAsync(
        Guid athleteId,
        string checksum,
        string fileName,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default);

    /// <summary>Returns null when the file is gone - a restored database against an empty volume.</summary>
    Task<Stream?> OpenAsync(
        Guid athleteId,
        string checksum,
        string fileName,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid athleteId,
        string checksum,
        string fileName,
        CancellationToken cancellationToken = default);
}

public interface IActivityFileParser
{
    SourceFormat Format { get; }

    /// <summary>True when this parser recognises the file by extension or magic bytes.</summary>
    bool CanParse(string fileName, ReadOnlySpan<byte> header);

    Task<ParsedActivity> ParseAsync(Stream stream, CancellationToken cancellationToken = default);
}

/// <summary>Selects the right parser for an uploaded file.</summary>
public interface IActivityFileParserFactory
{
    IActivityFileParser Resolve(string fileName, ReadOnlySpan<byte> header);
}

/// <summary>Input the advisor summarises; deliberately pre-aggregated, never raw samples.</summary>
public sealed record CoachingInput(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    IReadOnlyList<WeeklyTotals> Weeks,
    IReadOnlyList<CoachingActivitySummary> RecentActivities,
    int? MaxHeartRate);

public sealed record CoachingActivitySummary(
    DateOnly Date,
    string Sport,
    double DistanceKm,
    double MovingMinutes,
    double PaceSecondsPerKm,
    double GradeAdjustedPaceSecondsPerKm,
    double ElevationGainMeters,
    int? AverageHeartRateBpm);

public sealed record CoachingAnalysis(
    string Summary,
    TrainingLoadVerdict Verdict,
    IReadOnlyList<CoachingFinding> Findings,
    string ModelId);

/// <summary>
/// Generates a structured training assessment. Implementations must return the
/// shape above or fail; free-form prose is not an acceptable substitute.
/// </summary>
public interface ICoachingAdvisor
{
    bool IsConfigured { get; }

    Task<CoachingAnalysis> AnalyzeAsync(CoachingInput input, CancellationToken cancellationToken = default);
}
