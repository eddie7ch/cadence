using Cadence.Application.Abstractions;
using Cadence.Application.Common;
using Cadence.Application.Contracts;
using Cadence.Application.Mapping;
using Cadence.Domain.Athletes;

namespace Cadence.Application.Handlers;

/// <summary>
/// Backs <c>GET /auth/me</c>. A valid token whose subject no longer exists is a
/// deleted account, not an authentication failure, so it reports 404 rather than
/// inviting the client into a pointless re-login loop.
/// </summary>
public sealed class GetAthleteHandler
{
    private readonly IAthleteRepository _athletes;

    public GetAthleteHandler(IAthleteRepository athletes)
    {
        ArgumentNullException.ThrowIfNull(athletes);
        _athletes = athletes;
    }

    public async Task<Result<AthleteDto>> ExecuteAsync(
        Guid athleteId,
        CancellationToken cancellationToken = default)
    {
        Athlete? athlete = await _athletes.FindByIdAsync(athleteId, cancellationToken);

        return athlete is null
            ? Error.NotFound("Athlete not found.")
            : athlete.ToDto();
    }
}
