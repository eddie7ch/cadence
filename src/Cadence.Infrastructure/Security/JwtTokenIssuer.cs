using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Cadence.Application.Abstractions;
using Cadence.Domain.Athletes;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Cadence.Infrastructure.Security;

public sealed class JwtTokenIssuer : ITokenIssuer
{
    private readonly JwtOptions _options;
    private readonly IClock _clock;
    private readonly SigningCredentials _credentials;
    private readonly JwtSecurityTokenHandler _handler = new();

    public JwtTokenIssuer(IOptions<JwtOptions> options, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);

        // Resolving Value runs the registered validators, so a weak or missing
        // secret fails here - at the first attempt to construct the issuer -
        // rather than silently producing signable-by-anyone tokens.
        _options = options.Value;
        if (!_options.HasStrongSecret)
        {
            throw new InvalidOperationException(
                $"Jwt:Secret must be at least {JwtOptions.MinimumSecretBytes} bytes of UTF-8.");
        }

        _clock = clock;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret));
        _credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    public (string Token, int ExpiresInSeconds) Issue(Athlete athlete)
    {
        ArgumentNullException.ThrowIfNull(athlete);

        var issuedAt = _clock.UtcNow;
        var expiresAt = issuedAt.AddMinutes(_options.LifetimeMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, athlete.Id.ToString("D", CultureInfo.InvariantCulture)),
            new(JwtRegisteredClaimNames.Email, athlete.Email),
            new(JwtRegisteredClaimNames.Name, athlete.DisplayName),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture)),
            new(
                JwtRegisteredClaimNames.Iat,
                issuedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64),
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: issuedAt.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: _credentials);

        var expiresInSeconds = (int)Math.Max(0, (expiresAt - issuedAt).TotalSeconds);
        return (_handler.WriteToken(token), expiresInSeconds);
    }
}
