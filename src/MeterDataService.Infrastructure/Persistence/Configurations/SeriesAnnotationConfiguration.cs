using MeterDataService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MeterDataService.Infrastructure.Persistence.Configurations;

internal sealed class SeriesAnnotationConfiguration : IEntityTypeConfiguration<SeriesAnnotation>
{
    public void Configure(EntityTypeBuilder<SeriesAnnotation> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).UseIdentityAlwaysColumn();

        builder.Property(a => a.IntervalStart).HasColumnType("timestamptz");

        builder.Property(a => a.Type)
            .HasConversion<string>()
            .HasMaxLength(16);

        // 0–1 with five places: decimal(6,5).
        builder.Property(a => a.Score).HasPrecision(SeriesAnnotation.ScoreScale + 1, SeriesAnnotation.ScoreScale);

        // Not unique: spike and change point may both hit the same interval.
        builder.HasIndex(a => new { a.MeasurementSeriesId, a.IntervalStart });
    }
}
