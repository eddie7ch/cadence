using Cadence.Application.Abstractions;
using Cadence.Domain.Athletes;
using Cadence.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Infrastructure.Persistence.Repositories;

internal sealed class AthleteRepository : IAthleteRepository
{
    private readonly CadenceDbContext _context;

    public AthleteRepository(CadenceDbContext context) => _context = context;

    public Task<Athlete?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.Athletes.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<Athlete?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);

        var normalized = Normalize(email);

        // Matching the generated lower("Email") column rather than Email itself is what lets
        // this seek the unique index instead of scanning.
        return _context.Athletes
            .FirstOrDefaultAsync(
                a => EF.Property<string>(a, AthleteConfiguration.NormalizedEmail) == normalized,
                cancellationToken);
    }

    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);

        var normalized = Normalize(email);

        return _context.Athletes
            .AsNoTracking()
            .AnyAsync(
                a => EF.Property<string>(a, AthleteConfiguration.NormalizedEmail) == normalized,
                cancellationToken);
    }

    public void Add(Athlete athlete)
    {
        ArgumentNullException.ThrowIfNull(athlete);
        _context.Athletes.Add(athlete);
    }

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
