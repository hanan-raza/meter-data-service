using MeterDataService.Domain;
using MeterDataService.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace MeterDataService.Tests.Integration.Persistence;

[Trait("Category", "Integration")]
public sealed class MeasurementValuePersistenceTests(PostgreSqlFixture database) : IntegrationTestBase(database)
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    [SkippableFact]
    public async Task Measurement_value_round_trips_through_postgres()
    {
        var series = await SaveSeriesAsync(
            "41373559241",
            "DE0001234567890000000000000000001",
            s => s.AddValue(new DateTimeOffset(2026, 3, 2, 8, 15, 0, TimeSpan.FromHours(1)), 1.23456m, MeasurementStatus.Measured));
        var written = series.Values.Single();

        await using var context = CreateDbContext();
        var read = await context.MeasurementValues.SingleAsync(v => v.Id == written.Id);

        read.MeasurementSeriesId.ShouldBe(series.Id);
        read.IntervalStart.ShouldBe(written.IntervalStart);
        read.IntervalStart.Offset.ShouldBe(TimeSpan.Zero);
        read.Value.ShouldBe(1.23456m);
        read.Status.ShouldBe(MeasurementStatus.Measured);
    }

    [SkippableFact]
    public async Task Repeated_local_hour_on_fall_back_day_is_stored_as_two_distinct_intervals()
    {
        // 25 October 2026: local 02:00 occurs first in CEST (+02:00), then again in CET (+01:00).
        var summerTime = new DateTimeOffset(2026, 10, 25, 2, 0, 0, TimeSpan.FromHours(2));
        var winterTime = new DateTimeOffset(2026, 10, 25, 2, 0, 0, TimeSpan.FromHours(1));
        var series = await SaveSeriesAsync(
            "10000000009",
            "DE0001234567890000000000000000002",
            s =>
            {
                s.AddValue(summerTime, 0.25m, MeasurementStatus.Measured);
                s.AddValue(winterTime, 0.5m, MeasurementStatus.Estimated);
            });

        await using var context = CreateDbContext();
        var read = await context.MeasurementValues
            .Where(v => v.MeasurementSeriesId == series.Id)
            .OrderBy(v => v.IntervalStart)
            .ToListAsync();

        read.Count.ShouldBe(2);
        read.Select(v => TimeZoneInfo.ConvertTime(v.IntervalStart, Berlin).TimeOfDay)
            .ShouldAllBe(t => t == TimeSpan.FromHours(2));
        read.Select(v => v.Value).ShouldBe([0.25m, 0.5m]);
        read.Select(v => v.Status).ShouldBe([MeasurementStatus.Measured, MeasurementStatus.Estimated]);
    }

    // Each test uses its own MaLo/MeLo because both IDs are unique and the database is shared per class.
    private async Task<MeasurementSeries> SaveSeriesAsync(string maLoId, string meLoId, Action<MeasurementSeries> addValues)
    {
        var marketLocation = new MarketLocation(maLoId, EnergyDirection.Consumption);
        var series = marketLocation.AddMeterLocation(meLoId).AddSeries("1-1:1.29.0");
        addValues(series);

        await using var context = CreateDbContext();
        context.MarketLocations.Add(marketLocation);
        await context.SaveChangesAsync();
        return series;
    }
}
