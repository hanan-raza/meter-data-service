using MeterDataService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MeterDataService.Infrastructure.Persistence.Configurations;

internal sealed class MarketLocationConfiguration : IEntityTypeConfiguration<MarketLocation>
{
    public void Configure(EntityTypeBuilder<MarketLocation> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.MaLoId)
            .HasColumnName("malo_id")
            .HasMaxLength(MarketLocationId.Length)
            .IsFixedLength();
        builder.HasIndex(m => m.MaLoId).IsUnique();

        builder.Property(m => m.Direction)
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.HasMany(m => m.MeterLocations)
            .WithOne()
            .HasForeignKey(m => m.MarketLocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(m => m.MeterLocations).HasField("_meterLocations");
    }
}
