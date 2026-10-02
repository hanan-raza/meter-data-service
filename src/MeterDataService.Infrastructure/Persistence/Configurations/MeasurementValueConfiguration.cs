using MeterDataService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MeterDataService.Infrastructure.Persistence.Configurations;

internal sealed class MeasurementValueConfiguration : IEntityTypeConfiguration<MeasurementValue>
{
    public void Configure(EntityTypeBuilder<MeasurementValue> builder)
    {
        // bigint identity: a single market location produces ~35,000 values per year.
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).UseIdentityAlwaysColumn();

        builder.Property(v => v.IntervalStart).HasColumnType("timestamptz");
        builder.Ignore(v => v.IntervalEnd);

        builder.Property(v => v.Value).HasPrecision(MeasurementValue.Precision, MeasurementValue.Scale);

        builder.Property(v => v.Status)
            .HasConversion<string>()
            .HasMaxLength(16);

        // One value per interval; also the access path for every range query and aggregation.
        builder.HasIndex(v => new { v.MeasurementSeriesId, v.IntervalStart }).IsUnique();
    }
}
