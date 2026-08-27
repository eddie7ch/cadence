using System.Security.Claims;

namespace Cadence.Api.Infrastructure;

/// <summary>The athlete the current request is acting as.</summary>
public interface ICurrentAthlete
{
    /// <summary>Throws when the request carries no usable subject claim.</summary>
    Guid Id { get; }

    bool IsAuthenticated { get; }
}

public sealed class CurrentAthlete(IHttpContextAccessor httpContextAccessor) : ICurrentAthlete
{
    /// <remarks>
    /// JwtBearerOptions.MapInboundClaims is switched off in Program.cs so "sub"
    /// survives verbatim. The mapped WS-Federation URI is still consulted because
    /// a token issued by a differently configured handler arrives in that shape,
    /// and failing to find the athlete would look like a permissions bug.
    /// </remarks>
    private static readonly string[] SubjectClaimTypes = ["sub", ClaimTypes.NameIdentifier];

    public bool IsAuthenticated => TryResolve(out _);

    public Guid Id => TryResolve(out var id) ? id : throw new MissingAthleteClaimException();

    private bool TryResolve(out Guid athleteId)
    {
        athleteId = Guid.Empty;

        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated is not true)
        {
            return false;
        }

        foreach (var claimType in SubjectClaimTypes)
        {
            if (Guid.TryParse(principal.FindFirstValue(claimType), out athleteId))
            {
                return true;
            }
        }

        athleteId = Guid.Empty;
        return false;
    }
}

/// <summary>
/// Raised when an authorised endpoint cannot identify the caller. This is a fault,
/// not an expected outcome: authentication already succeeded, so a missing or
/// unparseable "sub" means the token issuer and the API disagree about the
/// contract, and returning a silent empty Guid would query another athlete's rows.
/// </summary>
public sealed class MissingAthleteClaimException()
    : InvalidOperationException(
        "The authenticated principal carries no parseable 'sub' claim, so the athlete could not be identified.");
