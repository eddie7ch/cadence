using System.Globalization;
using Cadence.Application.Abstractions;
using Cadence.Infrastructure.Caching;
using Cadence.Infrastructure.Coaching;
using Cadence.Infrastructure.Parsing;
using Cadence.Infrastructure.Persistence;
using Cadence.Infrastructure.Security;
using Cadence.Infrastructure.Storage;
using Cadence.Infrastructure.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Cadence.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddPersistence(Required(configuration.GetConnectionString("Postgres"), "ConnectionStrings:Postgres"));

        AddRedis(services, Required(configuration.GetConnectionString("Redis"), "ConnectionStrings:Redis"));
        AddSecurity(services, configuration);
        AddCoaching(services, configuration);

        AddStorage(services, configuration);

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IAnalyticsCache, RedisAnalyticsCache>();

        // Parsers hold no per-request state, so one instance each is enough; the
        // factory receives them as a set and picks by extension or magic bytes.
        services.AddSingleton<IActivityFileParser, GpxActivityParser>();
        services.AddSingleton<IActivityFileParser, FitActivityParser>();
        services.AddSingleton<IActivityFileParserFactory, ActivityFileParserFactory>();

        return services;
    }

    private static void AddStorage(IServiceCollection services, IConfiguration configuration)
    {
        string? directory = Value(configuration[$"{StorageOptions.SectionName}:UploadDirectory"]);

        services.AddOptions<StorageOptions>()
            .Configure(options => options.UploadDirectory = directory ?? options.UploadDirectory);

        services.AddSingleton<IActivityFileStore, FileSystemActivityFileStore>();
    }

    private static void AddRedis(IServiceCollection services, string connectionString)
    {
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(connectionString);

            // Without this, a Redis container that is a few seconds behind the API
            // container takes the whole API down at startup. The cache degrades to
            // computing values, so a missing Redis is a latency problem, not an
            // availability one - and the multiplexer reconnects on its own.
            options.AbortOnConnectFail = false;

            return ConnectionMultiplexer.Connect(options);
        });
    }

    private static void AddSecurity(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(JwtOptions.SectionName);

        services.AddOptions<JwtOptions>()
            .Configure(options =>
            {
                options.Secret = Value(section["Secret"]) ?? string.Empty;
                options.Issuer = Value(section["Issuer"]) ?? JwtOptions.DefaultIssuer;
                options.Audience = Value(section["Audience"]) ?? JwtOptions.DefaultAudience;
                options.LifetimeMinutes = Integer(section["LifetimeMinutes"]) ?? JwtOptions.DefaultLifetimeMinutes;
            })
            .Validate(
                options => options.HasStrongSecret,
                $"Jwt:Secret must be at least {JwtOptions.MinimumSecretBytes} bytes of UTF-8; a shorter HMAC-SHA256 key is padded rather than hashed and the tokens it signs are forgeable.")
            .Validate(
                options => options.LifetimeMinutes > 0,
                "Jwt:LifetimeMinutes must be greater than zero.");

        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();
    }

    private static void AddCoaching(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(AnthropicOptions.SectionName);

        services.AddOptions<AnthropicOptions>()
            .Configure(options =>
            {
                options.ApiKey = Value(section["ApiKey"]);
                options.Model = Value(section["Model"]) ?? AnthropicOptions.DefaultModel;
                options.MaxTokens = Integer(section["MaxTokens"]) ?? AnthropicOptions.DefaultMaxTokens;
            })
            .Validate(options => !string.IsNullOrWhiteSpace(options.Model), "Anthropic:Model must not be blank.")
            .Validate(options => options.MaxTokens > 0, "Anthropic:MaxTokens must be greater than zero.");

        services.AddSingleton<ICoachingAdvisor, AnthropicCoachingAdvisor>();
    }

    /// <summary>
    /// Compose substitutes an unset variable as an empty string, so "" has to mean
    /// absent everywhere - otherwise a blank value silently overrides its default.
    /// </summary>
    private static string? Value(string? raw) => string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();

    private static int? Integer(string? raw) =>
        Value(raw) is { } text && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static string Required(string? raw, string key) =>
        Value(raw) ?? throw new InvalidOperationException($"Configuration key '{key}' is required and was blank or absent.");
}
