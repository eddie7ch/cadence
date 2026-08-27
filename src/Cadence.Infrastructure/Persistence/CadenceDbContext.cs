using Cadence.Application.Abstractions;
using Cadence.Domain.Activities;
using Cadence.Domain.Athletes;
using Cadence.Domain.Coaching;
using Cadence.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Infrastructure.Persistence;

public sealed class CadenceDbContext : DbContext, IUnitOfWork
{
    public CadenceDbContext(DbContextOptions<CadenceDbContext> options)
        : base(options)
    {
    }

    public DbSet<Athlete> Athletes => Set<Athlete>();

    public DbSet<Activity> Activities => Set<Activity>();

    public DbSet<ActivitySample> ActivitySamples => Set<ActivitySample>();

    public DbSet<ActivitySplit> ActivitySplits => Set<ActivitySplit>();

    public DbSet<CoachingReport> CoachingReports => Set<CoachingReport>();

    /// <summary>
    /// Maps to PostgreSQL <c>date_trunc(text, timestamptz)</c>. The Npgsql provider has no
    /// built-in translation for it, and without one a weekly rollup degenerates into pulling
    /// every activity into memory to bucket it. Truncation of a <c>timestamptz</c> happens in
    /// the session time zone, which is UTC for the containers this runs against.
    /// Never invoked in process - it exists only to be translated.
    /// </summary>
    public static DateTime DateTrunc(string field, DateTimeOffset source) =>
        throw new InvalidOperationException($"{nameof(DateTrunc)} is only usable inside an EF Core query.");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        modelBuilder.HasPostgresExtension("postgis");

        // IsBuiltIn keeps the call unqualified; date_trunc lives in pg_catalog, not the model schema.
        modelBuilder
            .HasDbFunction(typeof(CadenceDbContext).GetMethod(
                nameof(DateTrunc),
                [typeof(string), typeof(DateTimeOffset)])!)
            .HasName("date_trunc")
            .IsBuiltIn();

        // Applied explicitly rather than by assembly scan: this assembly is shared with other
        // slices, and the model must not change shape because someone added a class elsewhere.
        modelBuilder.ApplyConfiguration(new AthleteConfiguration());
        modelBuilder.ApplyConfiguration(new ActivityConfiguration());
        modelBuilder.ApplyConfiguration(new ActivitySampleConfiguration());
        modelBuilder.ApplyConfiguration(new ActivitySplitConfiguration());
        modelBuilder.ApplyConfiguration(new CoachingReportConfiguration());
    }
}
