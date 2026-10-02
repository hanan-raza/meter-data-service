using MeterDataService.Domain;
using Microsoft.EntityFrameworkCore;

namespace MeterDataService.Infrastructure.Persistence;

public sealed class MeterDataDbContext(DbContextOptions<MeterDataDbContext> options) : DbContext(options)
{
    public DbSet<MarketLocation> MarketLocations => Set<MarketLocation>();

    public DbSet<MeterLocation> MeterLocations => Set<MeterLocation>();

    public DbSet<Meter> Meters => Set<Meter>();

    public DbSet<MeasurementSeries> MeasurementSeries => Set<MeasurementSeries>();

    public DbSet<MeasurementValue> MeasurementValues => Set<MeasurementValue>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MeterDataDbContext).Assembly);
}
