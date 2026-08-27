namespace Cadence.Domain.Athletes;

public sealed class Athlete
{
    private Athlete()
    {
        // EF Core materialisation.
        Email = null!;
        DisplayName = null!;
        PasswordHash = null!;
    }

    private Athlete(Guid id, string email, string displayName, string passwordHash, DateTimeOffset createdAt)
    {
        Id = id;
        Email = email;
        DisplayName = displayName;
        PasswordHash = passwordHash;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    /// <summary>Stored lower-cased; the unique index is on this value.</summary>
    public string Email { get; private set; }

    public string DisplayName { get; private set; }

    public string PasswordHash { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Measured maximum heart rate. Null means zones fall back to observed peaks.</summary>
    public int? MaxHeartRate { get; private set; }

    public int? RestingHeartRate { get; private set; }

    public int? BirthYear { get; private set; }

    public double? WeightKilograms { get; private set; }

    public static Athlete Register(string email, string displayName, string passwordHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        return new Athlete(Guid.CreateVersion7(), email.Trim().ToLowerInvariant(), displayName.Trim(), passwordHash, now);
    }

    public void UpdateProfile(int? maxHeartRate, int? restingHeartRate, int? birthYear, double? weightKilograms)
    {
        if (maxHeartRate is <= 0 or > 260)
        {
            throw new ArgumentOutOfRangeException(nameof(maxHeartRate), maxHeartRate, "Implausible maximum heart rate.");
        }

        if (restingHeartRate is <= 0 or > 150)
        {
            throw new ArgumentOutOfRangeException(nameof(restingHeartRate), restingHeartRate, "Implausible resting heart rate.");
        }

        if (maxHeartRate is { } max && restingHeartRate is { } rest && rest >= max)
        {
            throw new ArgumentException("Resting heart rate must be below maximum.", nameof(restingHeartRate));
        }

        MaxHeartRate = maxHeartRate;
        RestingHeartRate = restingHeartRate;
        BirthYear = birthYear;
        WeightKilograms = weightKilograms;
    }
}
