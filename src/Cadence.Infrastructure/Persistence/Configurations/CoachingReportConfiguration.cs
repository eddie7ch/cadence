using System.Text.Json;
using Cadence.Domain.Athletes;
using Cadence.Domain.Coaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Cadence.Infrastructure.Persistence.Configurations;

internal sealed class CoachingReportConfiguration : IEntityTypeConfiguration<CoachingReport>
{
    /// <summary>Backing field of <see cref="CoachingReport.Findings"/>.</summary>
    private const string FindingsField = "_findings";

    private static readonly JsonSerializerOptions FindingsJson = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<CoachingReport> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("CoachingReports");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.AthleteId).IsRequired();

        builder.Property(r => r.PeriodStart).HasColumnType("date");
        builder.Property(r => r.PeriodEnd).HasColumnType("date");

        builder.Property(r => r.Summary)
            .IsRequired()
            .HasMaxLength(4000);

        builder.Property(r => r.Verdict)
            .IsRequired()
            .HasMaxLength(16)
            .HasConversion<string>();

        builder.Property(r => r.ModelId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(r => r.GeneratedAt).HasColumnType("timestamp with time zone");
        builder.Property(r => r.ActivityCount);

        ConfigureFindings(builder);

        builder.HasOne<Athlete>()
            .WithMany()
            .HasForeignKey(r => r.AthleteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => new { r.AthleteId, r.GeneratedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_CoachingReports_AthleteId_GeneratedAt");
    }

    private static void ConfigureFindings(EntityTypeBuilder<CoachingReport> builder)
    {
        // Findings are only ever read back as a whole with their report - they are never
        // joined or filtered individually - so a child table would buy nothing and cost a
        // join. jsonb keeps them queryable from SQL if that ever changes.
        var converter = new ValueConverter<List<CoachingFinding>, string>(
            findings => JsonSerializer.Serialize(findings, FindingsJson),
            json => JsonSerializer.Deserialize<List<CoachingFinding>>(json, FindingsJson) ?? new List<CoachingFinding>());

        // Without a comparer EF compares the List by reference, so an in-place edit of the
        // collection would never be detected as a change.
        var comparer = new ValueComparer<List<CoachingFinding>>(
            (left, right) => left!.SequenceEqual(right!),
            findings => findings.Aggregate(0, (hash, finding) => HashCode.Combine(hash, finding.GetHashCode())),
            findings => findings.ToList());

        // The CLR property is IReadOnlyCollection with no setter; EF maps the List field.
        builder.Ignore(r => r.Findings);

        builder.Property<List<CoachingFinding>>(FindingsField)
            .HasField(FindingsField)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("Findings")
            .HasColumnType("jsonb")
            .HasConversion(converter, comparer)
            .IsRequired();
    }
}
