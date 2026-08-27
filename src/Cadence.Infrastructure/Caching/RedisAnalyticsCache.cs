using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cadence.Application.Abstractions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cadence.Infrastructure.Caching;

/// <summary>
/// Redis-backed analytics cache built around a per-athlete key version.
/// <para>
/// Every entry lives at <c>cadence:v{version}:athlete:{athleteId}:{suffix}</c>,
/// where <c>version</c> is an integer counter held at
/// <c>cadence:keyver:{athleteId}</c>. Invalidation is a single <c>INCR</c> of
/// that counter: the whole athlete moves to a new namespace, so every derived
/// entry - trends, zone splits, weekly rollups, whatever a future feature adds -
/// becomes unreachable at once, in O(1), without <c>SCAN</c> or <c>KEYS</c>.
/// Those two commands are the usual way this is done and both are traps: KEYS
/// blocks the whole server, and SCAN is a cursor walk over the entire keyspace
/// that gets slower as the deployment grows. Orphaned entries under an old
/// version are never read again and are reclaimed by their own TTL.
/// </para>
/// </summary>
public sealed class RedisAnalyticsCache : IAnalyticsCache
{
    private const string KeyPrefix = "cadence";

    /// <summary>
    /// Long enough for a slow analytics query, short enough that a process that
    /// dies mid-computation does not wedge the key for a noticeable time.
    /// </summary>
    private static readonly TimeSpan LockTtl = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan LockPollInterval = TimeSpan.FromMilliseconds(50);

    private static readonly TimeSpan LockWaitBudget = TimeSpan.FromSeconds(2);

    // Deleting unconditionally would let this caller release a lock that had
    // already expired and been re-acquired by someone else, which is exactly the
    // stampede the lock exists to prevent.
    private const string ReleaseLockScript =
        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisAnalyticsCache> _logger;

    public RedisAnalyticsCache(IConnectionMultiplexer redis, ILogger<RedisAnalyticsCache> logger)
    {
        ArgumentNullException.ThrowIfNull(redis);
        ArgumentNullException.ThrowIfNull(logger);

        _redis = redis;
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(Guid athleteId, string key, CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var database = _redis.GetDatabase();
            var physicalKey = await ResolveKeyAsync(database, athleteId, key).ConfigureAwait(false);
            return await ReadAsync<T>(database, physicalKey).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsRedisUnavailable(ex))
        {
            _logger.LogWarning(ex, "Redis unavailable reading {CacheKey}; treating it as a cache miss.", key);
            return null;
        }
    }

    public async Task SetAsync<T>(
        Guid athleteId,
        string key,
        T value,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var database = _redis.GetDatabase();
            var physicalKey = await ResolveKeyAsync(database, athleteId, key).ConfigureAwait(false);
            await WriteAsync(database, physicalKey, value, ttl).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsRedisUnavailable(ex))
        {
            _logger.LogWarning(ex, "Redis unavailable writing {CacheKey}; the value was not cached.", key);
        }
    }

    public async Task<T> GetOrCreateAsync<T>(
        Guid athleteId,
        string key,
        TimeSpan ttl,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);
        cancellationToken.ThrowIfCancellationRequested();

        IDatabase database;
        string physicalKey;

        try
        {
            database = _redis.GetDatabase();
            physicalKey = await ResolveKeyAsync(database, athleteId, key).ConfigureAwait(false);

            var hit = await ReadAsync<T>(database, physicalKey).ConfigureAwait(false);
            if (hit is not null)
            {
                return hit;
            }
        }
        catch (Exception ex) when (IsRedisUnavailable(ex))
        {
            // A cache outage must cost latency, never correctness or availability.
            _logger.LogWarning(ex, "Redis unavailable for {CacheKey}; computing the value directly.", key);
            return await factory(cancellationToken).ConfigureAwait(false);
        }

        var lockKey = physicalKey + ":lock";
        var lockToken = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

        bool acquired;
        try
        {
            acquired = await database
                .StringSetAsync(lockKey, lockToken, LockTtl, keepTtl: false, when: When.NotExists)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (IsRedisUnavailable(ex))
        {
            _logger.LogWarning(ex, "Redis unavailable locking {CacheKey}; computing the value directly.", key);
            return await factory(cancellationToken).ConfigureAwait(false);
        }

        if (!acquired)
        {
            var filledByPeer = await WaitForPeerAsync<T>(database, physicalKey, cancellationToken)
                .ConfigureAwait(false);
            if (filledByPeer is not null)
            {
                return filledByPeer;
            }

            // The holder is slower than the wait budget or died without publishing.
            // A duplicated computation beats a request that waits indefinitely.
            _logger.LogDebug("Lock on {CacheKey} not released within the wait budget; computing anyway.", key);
            var uncontendedValue = await factory(cancellationToken).ConfigureAwait(false);
            await TryWriteAsync(database, physicalKey, uncontendedValue, ttl, key).ConfigureAwait(false);
            return uncontendedValue;
        }

        try
        {
            var value = await factory(cancellationToken).ConfigureAwait(false);
            await TryWriteAsync(database, physicalKey, value, ttl, key).ConfigureAwait(false);
            return value;
        }
        finally
        {
            // Runs even when the factory throws or the token is cancelled, so a
            // failed computation never leaves the key locked for its full TTL.
            await ReleaseLockAsync(database, lockKey, lockToken).ConfigureAwait(false);
        }
    }

    public async Task InvalidateAthleteAsync(Guid athleteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var database = _redis.GetDatabase();
            var version = await database.StringIncrementAsync(VersionKey(athleteId)).ConfigureAwait(false);
            _logger.LogDebug(
                "Analytics namespace for athlete {AthleteId} advanced to version {Version}.",
                athleteId,
                version);
        }
        catch (Exception ex) when (IsRedisUnavailable(ex))
        {
            _logger.LogWarning(
                ex,
                "Redis unavailable invalidating athlete {AthleteId}; stale entries will expire by TTL instead.",
                athleteId);
        }
    }

    private async Task<T?> WaitForPeerAsync<T>(
        IDatabase database,
        string physicalKey,
        CancellationToken cancellationToken)
        where T : class
    {
        var deadline = DateTime.UtcNow + LockWaitBudget;

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(LockPollInterval, cancellationToken).ConfigureAwait(false);

            try
            {
                var value = await ReadAsync<T>(database, physicalKey).ConfigureAwait(false);
                if (value is not null)
                {
                    return value;
                }
            }
            catch (Exception ex) when (IsRedisUnavailable(ex))
            {
                _logger.LogWarning(ex, "Redis unavailable while waiting on {CacheKey}.", physicalKey);
                return null;
            }
        }

        return null;
    }

    private async Task<T?> ReadAsync<T>(IDatabase database, string physicalKey)
        where T : class
    {
        var raw = await database.StringGetAsync(physicalKey).ConfigureAwait(false);
        if (raw.IsNullOrEmpty)
        {
            return null;
        }

        string? json = raw;
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            // An entry written by an older shape of T is unreadable, not fatal:
            // drop it and let the caller recompute.
            _logger.LogWarning(ex, "Discarding unreadable cache entry {CacheKey}.", physicalKey);
            await TryDeleteAsync(database, physicalKey).ConfigureAwait(false);
            return null;
        }
    }

    private static Task WriteAsync<T>(IDatabase database, string physicalKey, T value, TimeSpan ttl)
        where T : class
    {
        var json = JsonSerializer.Serialize(value, SerializerOptions);
        var expiry = ttl > TimeSpan.Zero ? ttl : (TimeSpan?)null;
        return database.StringSetAsync(physicalKey, json, expiry, keepTtl: false);
    }

    private async Task TryWriteAsync<T>(
        IDatabase database,
        string physicalKey,
        T value,
        TimeSpan ttl,
        string logicalKey)
        where T : class
    {
        try
        {
            await WriteAsync(database, physicalKey, value, ttl).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsRedisUnavailable(ex))
        {
            _logger.LogWarning(ex, "Redis unavailable storing {CacheKey}; the computed value was not cached.", logicalKey);
        }
    }

    private async Task TryDeleteAsync(IDatabase database, string physicalKey)
    {
        try
        {
            await database.KeyDeleteAsync(physicalKey).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsRedisUnavailable(ex))
        {
            _logger.LogWarning(ex, "Redis unavailable deleting {CacheKey}.", physicalKey);
        }
    }

    private async Task ReleaseLockAsync(IDatabase database, string lockKey, string lockToken)
    {
        try
        {
            await database
                .ScriptEvaluateAsync(ReleaseLockScript, new RedisKey[] { lockKey }, new RedisValue[] { lockToken })
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (IsRedisUnavailable(ex))
        {
            // The lock's own TTL is the backstop.
            _logger.LogWarning(ex, "Redis unavailable releasing lock {LockKey}.", lockKey);
        }
    }

    private static async Task<string> ResolveKeyAsync(IDatabase database, Guid athleteId, string logicalKey)
    {
        var version = await ReadVersionAsync(database, athleteId).ConfigureAwait(false);
        return $"{KeyPrefix}:v{version.ToString(CultureInfo.InvariantCulture)}:athlete:{athleteId:D}:{logicalKey}";
    }

    private static async Task<long> ReadVersionAsync(IDatabase database, Guid athleteId)
    {
        var raw = await database.StringGetAsync(VersionKey(athleteId)).ConfigureAwait(false);
        return raw.TryParse(out long version) ? version : 0L;
    }

    private static string VersionKey(Guid athleteId) =>
        $"{KeyPrefix}:keyver:{athleteId:D}";


    private static bool IsRedisUnavailable(Exception ex) =>
        ex is RedisConnectionException or RedisTimeoutException;
}
