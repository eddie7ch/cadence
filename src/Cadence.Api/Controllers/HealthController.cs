using System.Reflection;
using Cadence.Application.Abstractions;
using Cadence.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Cadence.Api.Controllers;

/// <summary>Reported state of one backing service.</summary>
public sealed record DependencyStatusDto(string Name, string Status, string? Detail);

public sealed record ReadinessDto(
    string Status,
    IReadOnlyList<DependencyStatusDto> Dependencies,
    DateTimeOffset CheckedAt);

public sealed record LivenessDto(string Status, string Version, DateTimeOffset CheckedAt);

[ApiController]
[AllowAnonymous]
[Route("api/v1/health")]
[Produces("application/json")]
public sealed class HealthController(ILogger<HealthController> logger) : ControllerBase
{
    private const string Healthy = "healthy";
    private const string Unhealthy = "unhealthy";

    /// <summary>Liveness: the process is up and serving. Deliberately touches nothing.</summary>
    [HttpGet]
    [ProducesResponseType<LivenessDto>(StatusCodes.Status200OK)]
    public ActionResult<LivenessDto> Live()
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "unknown";

        return Ok(new LivenessDto(Healthy, version, DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Readiness: whether this instance can actually serve traffic. Dependencies are
    /// resolved per request rather than in the constructor so a liveness probe does
    /// not open a database connection.
    /// </summary>
    [HttpGet("ready")]
    [ProducesResponseType<ReadinessDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ReadinessDto>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ReadinessDto>> Ready(
        [FromServices] CadenceDbContext dbContext,
        [FromServices] IAnalyticsCache cache,
        [FromServices] ICoachingAdvisor advisor,
        [FromServices] IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(advisor);
        ArgumentNullException.ThrowIfNull(services);

        var postgres = await CheckPostgresAsync(dbContext, cancellationToken);
        var redis = await CheckRedisAsync(services, cache, cancellationToken);

        // A missing Anthropic key is a supported configuration, not a fault: the
        // platform is designed to run without it and only coaching reports are
        // unavailable. Reporting it as unhealthy would take the whole API out of
        // rotation over an optional feature.
        var llm = advisor.IsConfigured
            ? new DependencyStatusDto("llm", Healthy, "configured")
            : new DependencyStatusDto("llm", "not-configured", "Anthropic:ApiKey is blank, so coaching reports are unavailable.");

        var ready = postgres.Status == Healthy && redis.Status == Healthy;

        var response = new ReadinessDto(
            ready ? "ready" : "not-ready",
            [postgres, redis, llm],
            DateTimeOffset.UtcNow);

        return ready
            ? Ok(response)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, response);
    }

    private async Task<DependencyStatusDto> CheckPostgresAsync(
        CadenceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.Database.CanConnectAsync(cancellationToken)
                ? new DependencyStatusDto("postgres", Healthy, "reachable")
                : new DependencyStatusDto("postgres", Unhealthy, "the server refused the connection");
        }
        catch (Exception ex)
        {
            return Failed("postgres", ex);
        }
    }

    private async Task<DependencyStatusDto> CheckRedisAsync(
        IServiceProvider services,
        IAnalyticsCache cache,
        CancellationToken cancellationToken)
    {
        try
        {
            if (services.GetService<IConnectionMultiplexer>() is { } multiplexer)
            {
                var latency = await multiplexer.GetDatabase().PingAsync();
                return new DependencyStatusDto(
                    "redis",
                    Healthy,
                    $"ping {latency.TotalMilliseconds:F1} ms");
            }

            // No multiplexer to ask directly, so prove the cache round-trips instead
            // of inferring health from a read alone - a cache that silently drops
            // writes reads exactly like an empty healthy one.
            // A throwaway athlete id: the probe writes into its own namespace so a
            // readiness check can never collide with, or invalidate, real analytics.
            var probeAthlete = Guid.CreateVersion7();
            var probe = probeAthlete.ToString("n");
            await cache.SetAsync(probeAthlete, "health:ready", probe, TimeSpan.FromSeconds(30), cancellationToken);
            var echoed = await cache.GetAsync<string>(probeAthlete, "health:ready", cancellationToken);

            return string.Equals(echoed, probe, StringComparison.Ordinal)
                ? new DependencyStatusDto("redis", Healthy, "round-trip ok")
                : new DependencyStatusDto("redis", Unhealthy, "the cache did not return the value just written");
        }
        catch (Exception ex)
        {
            return Failed("redis", ex);
        }
    }

    /// <remarks>
    /// The exception type, never its message. Readiness is anonymous, and driver
    /// messages routinely quote host names, ports, and credentials.
    /// </remarks>
    private DependencyStatusDto Failed(string name, Exception exception)
    {
        logger.LogError(exception, "Readiness check for {Dependency} failed.", name);
        return new DependencyStatusDto(name, Unhealthy, exception.GetType().Name);
    }
}
