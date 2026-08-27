using Cadence.Domain.Activities;
using Cadence.Domain.Athletes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Infrastructure.Persistence.Configurations;

internal sealed class ActivityConfiguration : IEntityTypeConfiguration<Activity>
{
    public void Configure(EntityTypeBuilder<Activity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Activities");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        // Derived from AveragePaceSecondsPerKm; there is no column and no backing field for it.
        builder.Ignore(a => a.AveragePace);

        builder.Property(a => a.AthleteId).IsRequired();

        builder.Property(a => a.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(a => a.Sport)
            .IsRequired()
            .HasMaxLength(32)
            .HasConversion<string>();

        builder.Property(a => a.Status)
            .IsRequired()
            .HasMaxLength(16)
            .HasConversion<string>();

        builder.Property(a => a.SourceFormat)
            .IsRequired()
            .HasMaxLength(16)
            .HasConversion<string>();

        builder.Property(a => a.SourceFileName)
            .IsRequired()
            .HasMaxLength(260);

        builder.Property(a => a.SourceChecksum)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(a => a.Error).HasMaxLength(2000);

        builder.Property(a => a.CreatedAt).HasColumnType("timestamp with time zone");
        builder.Property(a => a.ProcessedAt).HasColumnType("timestamp with time zone");
        builder.Property(a => a.StartedAt).HasColumnType("timestamp with time zone");

        builder.Property(a => a.ElapsedTime).HasColumnType("interval");
        builder.Property(a => a.MovingTime).HasColumnType("interval");

        builder.Property(a => a.Route).HasColumnType("geometry(LineString, 4326)");
        builder.Property(a => a.SimplifiedRoute).HasColumnType("geometry(LineString, 4326)");

        builder.HasOne<Athlete>()
            .WithMany()
            .HasForeignKey(a => a.AthleteId)
            .OnDelete(DeleteBehavior.Cascade);

        ConfigureChildCollections(builder);

        // Re-uploading the same file must be a no-op rather than a duplicate row.
        builder.HasIndex(a => new { a.AthleteId, a.SourceChecksum })
            .IsUnique()
            .HasDatabaseName("IX_Activities_AthleteId_SourceChecksum");

        builder.HasIndex(a => new { a.AthleteId, a.StartedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_Activities_AthleteId_StartedAt");

        // Without this every "what did I run near here" query is a sequential scan
        // plus a geometry comparison per row.
        builder.HasIndex(a => a.Route)
            .HasMethod("gist")
            .HasDatabaseName("IX_Activities_Route");
    }

    private static void ConfigureChildCollections(EntityTypeBuilder<Activity> builder)
    {
        builder.HasMany(a => a.Samples)
            .WithOne()
            .HasForeignKey(s => s.ActivityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(a => a.Splits)
            .WithOne()
            .HasForeignKey(s => s.ActivityId)
            .OnDelete(DeleteBehavior.Cascade);

        // The public surface is IReadOnlyCollection with no mutator, so EF must read and
        // write the List fields directly instead of going through the property.
        builder.Navigation(a => a.Samples)
            .HasField("_samples")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Navigation(a => a.Splits)
            .HasField("_splits")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
