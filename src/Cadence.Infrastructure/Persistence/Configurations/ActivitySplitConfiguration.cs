using Cadence.Domain.Activities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Infrastructure.Persistence.Configurations;

internal sealed class ActivitySplitConfiguration : IEntityTypeConfiguration<ActivitySplit>
{
    public void Configure(EntityTypeBuilder<ActivitySplit> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ActivitySplits");

        builder.HasKey(s => new { s.ActivityId, s.Number });

        builder.Property(s => s.Number).ValueGeneratedNever();

        builder.Property(s => s.Duration).HasColumnType("interval");

        builder.Property(s => s.DistanceMeters);
        builder.Property(s => s.PaceSecondsPerKm);
        builder.Property(s => s.GradeAdjustedPaceSecondsPerKm);
        builder.Property(s => s.ElevationGainMeters);
        builder.Property(s => s.AverageHeartRateBpm);
        builder.Property(s => s.IsComplete);
    }
}
