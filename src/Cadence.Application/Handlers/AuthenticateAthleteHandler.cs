using Cadence.Application.Abstractions;
using Cadence.Application.Common;
using Cadence.Application.Contracts;
using Cadence.Application.Mapping;
using Cadence.Domain.Athletes;

namespace Cadence.Application.Handlers;

public sealed class AuthenticateAthleteHandler
{
    private readonly IAthleteRepository _athletes;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenIssuer _tokenIssuer;

    public AuthenticateAthleteHandler(
        IAthleteRepository athletes,
        IPasswordHasher passwordHasher,
        ITokenIssuer tokenIssuer)
    {
        ArgumentNullException.ThrowIfNull(athletes);
        ArgumentNullException.ThrowIfNull(passwordHasher);
        ArgumentNullException.ThrowIfNull(tokenIssuer);

        _athletes = athletes;
        _passwordHasher = passwordHasher;
        _tokenIssuer = tokenIssuer;
    }

    public async Task<Result<AuthResponseDto>> ExecuteAsync(
        string? email,
        string? password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            return InvalidCredentials();
        }

        Athlete? athlete = await _athletes.FindByEmailAsync(email.Trim().ToLowerInvariant(), cancellationToken);
        if (athlete is null || !_passwordHasher.Verify(password, athlete.PasswordHash))
        {
            return InvalidCredentials();
        }

        (string token, int expiresInSeconds) = _tokenIssuer.Issue(athlete);
        return new AuthResponseDto(token, "Bearer", expiresInSeconds, athlete.ToDto());
    }

    /// <summary>
    /// One message for both "no such account" and "wrong password", so the
    /// sign-in endpoint cannot be used to enumerate which addresses are
    /// registered.
    /// </summary>
    private static Error InvalidCredentials() => Error.Unauthorized("Email or password is incorrect.");
}
