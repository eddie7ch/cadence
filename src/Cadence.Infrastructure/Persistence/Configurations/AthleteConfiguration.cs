using Cadence.Domain.Athletes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Infrastructure.Persistence.Configurations;

internal sealed class AthleteConfiguration : IEntityTypeConfiguration<Athlete>
{
    /// <summary>
    /// Shadow column carrying <c>lower("Email")</c>. EF Core cannot express an expression
    /// index, so the expression is materialised as a stored generated column and the unique
    /// index is placed on that - which is the same guarantee as
    /// <c>CREATE UNIQUE INDEX ... ON "Athletes" (lower("Email"))</c>, and additionally gives
    /// lookups a column they can actually seek on.
    /// </summary>
    internal const string NormalizedEmail = "NormalizedEmail";

    public void Configure(EntityTypeBuilder<Athlete> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Athletes");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.Email)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(a => a.DisplayName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(a => a.PasswordHash)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(a => a.CreatedAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(a => a.MaxHeartRate);
        builder.Property(a => a.RestingHeartRate);
        builder.Property(a => a.BirthYear);
        builder.Property(a => a.WeightKilograms);

        builder.Property<string>(NormalizedEmail)
            .HasMaxLength(256)
            .HasComputedColumnSql(@"lower(""Email"")", stored: true);

        builder.HasIndex(NormalizedEmail)
            .IsUnique()
            .HasDatabaseName("IX_Athletes_Email_Lower");
    }
}
