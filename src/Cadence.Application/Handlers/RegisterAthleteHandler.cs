using System.Net.Mail;
using Cadence.Application.Abstractions;
using Cadence.Application.Common;
using Cadence.Application.Contracts;
using Cadence.Application.Mapping;
using Cadence.Domain.Athletes;

namespace Cadence.Application.Handlers;

public sealed class RegisterAthleteHandler
{
    public const int MinimumPasswordLength = 8;

    /// <summary>
    /// An upper bound exists only so an attacker cannot make the server spend
    /// unbounded CPU hashing a megabyte-long "password".
    /// </summary>
    public const int MaximumPasswordLength = 256;

    private readonly IAthleteRepository _athletes;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenIssuer _tokenIssuer;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public RegisterAthleteHandler(
        IAthleteRepository athletes,
        IPasswordHasher passwordHasher,
        ITokenIssuer tokenIssuer,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(athletes);
        ArgumentNullException.ThrowIfNull(passwordHasher);
        ArgumentNullException.ThrowIfNull(tokenIssuer);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(clock);

        _athletes = athletes;
        _passwordHasher = passwordHasher;
        _tokenIssuer = tokenIssuer;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<AuthResponseDto>> ExecuteAsync(
        string? email,
        string? displayName,
        string? password,
        CancellationToken cancellationToken = default)
    {
        if (NormalizeEmail(email) is not { } normalizedEmail)
        {
            return Error.Validation("A valid email address is required.");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return Error.Validation("A display name is required.");
        }

        if (password is null || password.Length is < MinimumPasswordLength or > MaximumPasswordLength)
        {
            return Error.Validation(
                $"The password must be between {MinimumPasswordLength} and {MaximumPasswordLength} characters.");
        }

        if (await _athletes.EmailExistsAsync(normalizedEmail, cancellationToken))
        {
            return Error.Conflict("An account with that email address already exists.");
        }

        Athlete athlete = Athlete.Register(
            normalizedEmail,
            displayName,
            _passwordHasher.Hash(password),
            _clock.UtcNow);

        _athletes.Add(athlete);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        (string token, int expiresInSeconds) = _tokenIssuer.Issue(athlete);
        return new AuthResponseDto(token, "Bearer", expiresInSeconds, athlete.ToDto());
    }

    /// <summary>
    /// Returns the lower-cased address, or null when the input is not a bare
    /// mailbox. The length comparison rejects the display-name forms
    /// <c>MailAddress</c> also accepts ("Ada &lt;ada@example.com&gt;"), which
    /// would otherwise be stored verbatim and never match at sign-in.
    /// </summary>
    private static string? NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        string trimmed = email.Trim();
        return MailAddress.TryCreate(trimmed, out MailAddress? parsed) && parsed.Address.Length == trimmed.Length
            ? trimmed.ToLowerInvariant()
            : null;
    }
}
