using MeterDataService.Domain;
using MeterDataService.Infrastructure;
using MeterDataService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;

namespace MeterDataService.Tests.Unit.Persistence;

/// <summary>
/// Checks the relational model without a database: building the model never opens a connection.
/// Column types matter because billing relies on exact decimals and unambiguous instants.
/// </summary>
public sealed class MeterDataModelTests : IDisposable
{
    private readonly MeterDataDbContext _context = new(
        new DbContextOptionsBuilder<MeterDataDbContext>()
            .UseMeterDataStore("Host=localhost;Database=model_only")
            .Options);

    // The runtime model drops annotations such as column types; the design-time model keeps them.
    private IModel Model => _context.GetService<IDesignTimeModel>().Model;

    public void Dispose() => _context.Dispose();

    [Fact]
    public void Energy_value_is_numeric_18_5()
    {
        Column<MeasurementValue>(nameof(MeasurementValue.Value)).ShouldBe("numeric(18,5)");
    }

    [Theory]
    [InlineData(typeof(MeasurementValue), nameof(MeasurementValue.IntervalStart))]
    [InlineData(typeof(Meter), nameof(Meter.InstalledAt))]
    [InlineData(typeof(Meter), nameof(Meter.RemovedAt))]
    public void Timestamps_are_timestamptz(Type entity, string property)
    {
        Model.FindEntityType(entity)!.FindProperty(property)!.GetColumnType().ShouldBe("timestamptz");
    }

    [Fact]
    public void Measurement_status_is_stored_as_readable_text()
    {
        Column<MeasurementValue>(nameof(MeasurementValue.Status)).ShouldBe("character varying(16)");
    }

    [Fact]
    public void Tables_and_columns_use_snake_case()
    {
        var entity = Model.FindEntityType(typeof(MeasurementValue))!;

        entity.GetTableName().ShouldBe("measurement_values");
        entity.FindProperty(nameof(MeasurementValue.MeasurementSeriesId))!
            .GetColumnName().ShouldBe("measurement_series_id");
    }

    [Fact]
    public void Only_one_value_per_series_and_interval()
    {
        var index = Model.FindEntityType(typeof(MeasurementValue))!.GetIndexes()
            .Single(i => i.Properties.Any(p => p.Name == nameof(MeasurementValue.IntervalStart)));

        index.IsUnique.ShouldBeTrue();
        index.Properties.Select(p => p.Name).ShouldBe(
            [nameof(MeasurementValue.MeasurementSeriesId), nameof(MeasurementValue.IntervalStart)]);
    }

    [Fact]
    public void Market_location_id_is_unique()
    {
        var index = Model.FindEntityType(typeof(MarketLocation))!.GetIndexes()
            .Single(i => i.Properties.Single().Name == nameof(MarketLocation.MaLoId));

        index.IsUnique.ShouldBeTrue();
    }

    [Fact]
    public void Computed_interval_end_is_not_persisted()
    {
        Model.FindEntityType(typeof(MeasurementValue))!
            .FindProperty(nameof(MeasurementValue.IntervalEnd)).ShouldBeNull();
    }

    private string? Column<TEntity>(string property) =>
        Model.FindEntityType(typeof(TEntity))!.FindProperty(property)!.GetColumnType();
}
