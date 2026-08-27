using Cadence.Domain.Activities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Infrastructure.Persistence.Configurations;

internal sealed class ActivitySampleConfiguration : IEntityTypeConfiguration<ActivitySample>
{
    public void Configure(EntityTypeBuilder<ActivitySample> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ActivitySamples");

        // Natural key: samples are always addressed by their position in one activity, and a
        // surrogate id on the widest table in the schema would be pure storage overhead.
        builder.HasKey(s => new { s.ActivityId, s.Sequence });

        builder.Property(s => s.Sequence).ValueGeneratedNever();

        builder.Property(s => s.Timestamp).HasColumnType("timestamp with time zone");

        builder.Property(s => s.Location)
            .IsRequired()
            .HasColumnType("geometry(Point, 4326)");

        builder.Property(s => s.ElapsedSeconds);
        builder.Property(s => s.CumulativeDistanceMeters);
        builder.Property(s => s.AltitudeMeters);
        builder.Property(s => s.HeartRateBpm);
        builder.Property(s => s.CadenceRpm);
        builder.Property(s => s.PowerWatts);
        builder.Property(s => s.SpeedMetersPerSecond);
        builder.Property(s => s.TemperatureCelsius);
    }
}
