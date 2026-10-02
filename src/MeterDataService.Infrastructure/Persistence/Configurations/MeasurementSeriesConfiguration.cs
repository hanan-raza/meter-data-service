using MeterDataService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MeterDataService.Infrastructure.Persistence.Configurations;

internal sealed class MeasurementSeriesConfiguration : IEntityTypeConfiguration<MeasurementSeries>
{
    public void Configure(EntityTypeBuilder<MeasurementSeries> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.ObisCode).HasMaxLength(MeasurementSeries.ObisCodeMaxLength);
        builder.HasIndex(s => new { s.MeterLocationId, s.ObisCode }).IsUnique();

        builder.HasMany(s => s.Values)
            .WithOne()
            .HasForeignKey(v => v.MeasurementSeriesId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Values).HasField("_values");
    }
}
