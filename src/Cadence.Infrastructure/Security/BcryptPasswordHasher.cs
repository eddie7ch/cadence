using BCrypt.Net;
using Cadence.Application.Abstractions;
using BC = BCrypt.Net.BCrypt;

namespace Cadence.Infrastructure.Security;

public sealed class BcryptPasswordHasher : IPasswordHasher
{
    // 2^12 rounds: roughly a quarter of a second per hash on current server
    // hardware, which is slow enough to make offline cracking expensive and fast
    // enough that a login does not feel stalled.
    private const int WorkFactor = 12;

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        return BC.HashPassword(password, WorkFactor);
    }

    public bool Verify(string password, string hash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hash))
        {
            return false;
        }

        try
        {
            return BC.Verify(password, hash);
        }
        catch (SaltParseException)
        {
            // A stored hash that BCrypt cannot parse is a corrupt or foreign
            // credential, not an exceptional condition: it simply does not match.
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
