using System.Text;
using System.Text.Json.Serialization;
using Cadence.Api.Infrastructure;
using Cadence.Application;
using Cadence.Infrastructure;
using Cadence.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

const string WebCorsPolicy = "web";

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;

        // DictionaryKeyPolicy is deliberately left alone: the only dictionary on the
        // wire is heart-rate zone seconds, whose keys are data, not property names.

        // Enums cross the wire as names. Ordinals would silently change meaning the
        // first time a value is inserted into the middle of an enum.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddProblemDetails();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentAthlete, CurrentAthlete>();

// The queue is shared between request threads and the worker, so it has to be a
// singleton; the worker reads from that same instance.
builder.Services.AddSingleton<ActivityProcessingQueue>();
builder.Services.AddHostedService<ActivityProcessingWorker>();

var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtSecret = jwtSection["Secret"];

// Compose substitutes an unset variable as an empty string, so blank has to fail
// exactly the way absent does rather than signing tokens with "".
if (string.IsNullOrWhiteSpace(jwtSecret))
{
    throw new InvalidOperationException(
        "Jwt:Secret is not configured. Set the Jwt__Secret environment variable to a value of at least 32 bytes.");
}

if (Encoding.UTF8.GetByteCount(jwtSecret) < 32)
{
    throw new InvalidOperationException(
        "Jwt:Secret is shorter than the 32 bytes HMAC-SHA256 requires; a shorter key is rejected at signing time.");
}

var jwtIssuer = jwtSection["Issuer"] is { Length: > 0 } issuer ? issuer : "cadence";
var jwtAudience = jwtSection["Audience"] is { Length: > 0 } audience ? audience : "cadence";

// Built out here rather than inside the lambda: nullable flow analysis does not
// cross into a closure, so the null check above would not be visible in there.
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Without this the handler rewrites "sub" to the WS-Federation
        // nameidentifier URI and CurrentAthlete would never find the claim the
        // contract says carries the athlete id.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateLifetime = true,

            // The default five minutes would keep expired tokens working; thirty
            // seconds is enough for ordinary clock drift between containers.
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
        };
    });

builder.Services.AddAuthorization(options =>
{
    // Authentication is the default. Anonymous access is opt-in per endpoint, so
    // adding a controller cannot accidentally publish an athlete's data.
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

string[] allowedOrigins =
    builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() is { Length: > 0 } origins
        ? origins
        : ["http://localhost:5173"];

builder.Services.AddCors(options => options.AddPolicy(
    WebCorsPolicy,
    policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .WithExposedHeaders("Location")));

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Cadence API",
        Version = "v1",
        Description = "Spatial and time-series athletic performance platform.",
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the accessToken returned by /api/v1/auth/login.",
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = [],
    });
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await ApplyMigrationsAsync(app);
}

app.UseMiddleware<ExceptionHandlingMiddleware>();

// Left on outside Development on purpose: this is a portfolio API whose whole
// surface is meant to be browsable by a reviewer.
app.UseSwagger();
app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Cadence API v1"));

app.UseCors(WebCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

await app.RunAsync();

static async Task ApplyMigrationsAsync(WebApplication host)
{
    // Development only. A production deployment migrates as a separate step,
    // because two instances rolling at once would race each other into the same
    // schema change.
    await using var scope = host.Services.CreateAsyncScope();
    var logger = scope.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("Cadence.Api.Startup");

    try
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<CadenceDbContext>();
        await dbContext.Database.MigrateAsync();
        logger.LogInformation("Database migrations applied.");
    }
    catch (Exception ex)
    {
        // Postgres usually loses the startup race in Compose. Rethrowing would turn
        // a few seconds of waiting into a container restart loop, so the API starts
        // and the readiness probe reports the database as unhealthy until it is up.
        logger.LogError(
            ex,
            "Could not apply database migrations at startup. The API is starting anyway; check /api/v1/health/ready.");
    }
}

/// <summary>Named so WebApplicationFactory can bootstrap the API in integration tests.</summary>
public partial class Program;
