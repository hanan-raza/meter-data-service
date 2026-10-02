using MeterDataService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MeterDataService.Infrastructure.Persistence.Configurations;

internal sealed class MeterLocationConfiguration : IEntityTypeConfiguration<MeterLocation>
{
    public void Configure(EntityTypeBuilder<MeterLocation> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.MeLoId)
            .HasMaxLength(MeterLocationId.Length)
            .IsFixedLength();
        builder.HasIndex(m => m.MeLoId).IsUnique();

        builder.HasMany(m => m.Meters)
            .WithOne()
            .HasForeignKey(m => m.MeterLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(m => m.Meters).HasField("_meters");

        builder.HasMany(m => m.Series)
            .WithOne()
            .HasForeignKey(s => s.MeterLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(m => m.Series).HasField("_series");
    }
}
