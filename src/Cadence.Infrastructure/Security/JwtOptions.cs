using System.Text;

namespace Cadence.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public const string DefaultIssuer = "cadence";

    public const string DefaultAudience = "cadence";

    public const int DefaultLifetimeMinutes = 720;

    /// <summary>HMAC-SHA256 signing key. Must be at least <see cref="MinimumSecretBytes"/> bytes.</summary>
    public const int MinimumSecretBytes = 32;

    public string Secret { get; set; } = string.Empty;

    public string Issuer { get; set; } = DefaultIssuer;

    public string Audience { get; set; } = DefaultAudience;

    public int LifetimeMinutes { get; set; } = DefaultLifetimeMinutes;

    /// <summary>
    /// HMAC-SHA256 has a 256-bit block, so a key shorter than that is padded
    /// rather than hashed - the effective key space shrinks to whatever was
    /// supplied. Anything under 32 bytes is a forgeable token, not a warning.
    /// </summary>
    public bool HasStrongSecret =>
        !string.IsNullOrWhiteSpace(Secret) && Encoding.UTF8.GetByteCount(Secret) >= MinimumSecretBytes;
}
