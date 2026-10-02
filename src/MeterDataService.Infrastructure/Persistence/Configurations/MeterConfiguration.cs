using MeterDataService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MeterDataService.Infrastructure.Persistence.Configurations;

internal sealed class MeterConfiguration : IEntityTypeConfiguration<Meter>
{
    public void Configure(EntityTypeBuilder<Meter> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.SerialNumber).HasMaxLength(Meter.SerialNumberMaxLength);
        builder.HasIndex(m => m.SerialNumber);

        builder.Property(m => m.InstalledAt).HasColumnType("timestamptz");
        builder.Property(m => m.RemovedAt).HasColumnType("timestamptz");

        // The domain allows only one installed meter per location; the partial index enforces it under concurrency.
        builder.HasIndex(m => m.MeterLocationId)
            .IsUnique()
            .HasFilter("removed_at IS NULL")
            .HasDatabaseName("ix_meters_meter_location_id_active");
    }
}
