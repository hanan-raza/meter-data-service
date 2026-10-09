using MeterDataService.Application.Aggregation;
using MeterDataService.Domain;
using MeterDataService.Infrastructure;
using MeterDataService.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MeterDataService.Tests.Integration.Persistence;

[Trait("Category", "Integration")]
public sealed class AggregationRepositoryTests(PostgreSqlFixture database) : IntegrationTestBase(database), IDisposable
{
    private static readonly TimeSpan Cet = TimeSpan.FromHours(1);
    private static readonly TimeSpan Cest = TimeSpan.FromHours(2);

    private static readonly DateOnly FallBackDay = new(2026, 10, 25);
    private static readonly DateOnly SpringForwardDay = new(2026, 3, 29);

    private ServiceProvider? _services;
    private IServiceScope? _scope;

    [SkippableFact]
    public async Task Daily_totals_follow_german_days_including_the_25_hour_day()
    {
        const string maLo = "41373559241";
        await SaveSeriesAsync(maLo, s => FillDays(s, FallBackDay.AddDays(-1), FallBackDay.AddDays(1), 0.1m));

        var totals = await Repository().GetDailyTotalsAsync(maLo, FallBackDay.AddDays(-1), FallBackDay.AddDays(1), CancellationToken.None);

        totals.Select(t => t.EnergyKwh).ShouldBe([9.6m, 10.0m, 9.6m]);
        totals.Select(t => t.Intervals).ShouldBe([96, 100, 96]);
        totals[1].Start.ShouldBe(new DateTimeOffset(2026, 10, 25, 0, 0, 0, Cest));
        totals[1].Start.Offset.ShouldBe(Cest);
        totals[1].End.ShouldBe(new DateTimeOffset(2026, 10, 26, 0, 0, 0, Cet));
        totals[1].End.Offset.ShouldBe(Cet);
        (totals[1].End - totals[1].Start).ShouldBe(TimeSpan.FromHours(25));
    }

    [SkippableFact]
    public async Task Spring_forward_day_is_a_23_hour_day_with_92_intervals()
    {
        const string maLo = "10000000009";
        await SaveSeriesAsync(maLo, s => FillDays(s, SpringForwardDay, SpringForwardDay, 0.1m));

        var total = (await Repository().GetDailyTotalsAsync(maLo, SpringForwardDay, SpringForwardDay, CancellationToken.None)).ShouldHaveSingleItem();

        total.EnergyKwh.ShouldBe(9.2m);
        total.Intervals.ShouldBe(92);
        (total.End - total.Start).ShouldBe(TimeSpan.FromHours(23));
    }

    [SkippableFact]
    public async Task Hourly_totals_keep_both_passes_of_the_repeated_hour_apart()
    {
        const string maLo = "20000000008";
        await SaveSeriesAsync(maLo, s => FillDays(s, FallBackDay, FallBackDay, 0.1m));

        var totals = await Repository().GetHourlyTotalsAsync(maLo, FallBackDay, FallBackDay, CancellationToken.None);

        totals.Count.ShouldBe(25);
        totals.ShouldAllBe(t => t.EnergyKwh == 0.4m && t.Intervals == 4);
        totals.Where(t => t.Start.Hour == 2).Select(t => t.Start.Offset).ShouldBe([Cest, Cet]);
        totals.Select(t => t.End).ShouldBe(totals.Skip(1).Select(t => t.Start).Append(new DateTimeOffset(2026, 10, 26, 0, 0, 0, Cet)));
    }

    [SkippableFact]
    public async Task Monthly_totals_assign_local_midnight_on_the_first_to_the_new_month()
    {
        // Local 2026-10-01 00:00 is 2026-09-30 22:00 UTC: a UTC month boundary would count it for September.
        const string maLo = "30000000007";
        var september = new DateOnly(2026, 9, 30);
        var october = new DateOnly(2026, 10, 1);
        await SaveSeriesAsync(maLo, s =>
        {
            FillDays(s, september, september, 0.1m);
            FillDays(s, october, october, 0.2m);
        });

        var totals = await Repository().GetMonthlyTotalsAsync(maLo, september, october, CancellationToken.None);

        totals.Select(t => t.EnergyKwh).ShouldBe([9.6m, 19.2m]);
        totals.Select(t => t.Start).ShouldBe([new DateTimeOffset(2026, 9, 1, 0, 0, 0, Cest), new DateTimeOffset(2026, 10, 1, 0, 0, 0, Cest)]);
        totals[1].End.ShouldBe(new DateTimeOffset(2026, 11, 1, 0, 0, 0, Cet));
    }

    [SkippableFact]
    public async Task Totals_are_exact_decimal_sums()
    {
        // As double, 0.33333 + 0.33333 + 0.33333 + 0.00001 is 0.9999999999999999 or so; as numeric it is 1.
        const string maLo = "40000000006";
        var day = new DateOnly(2026, 6, 10);
        var start = GermanCalendar.StartOfDayUtc(day).AddHours(10);
        await SaveSeriesAsync(maLo, s =>
        {
            s.AddValue(start, 0.33333m, MeasurementStatus.Measured);
            s.AddValue(start.AddMinutes(15), 0.33333m, MeasurementStatus.Measured);
            s.AddValue(start.AddMinutes(30), 0.33333m, MeasurementStatus.Measured);
            s.AddValue(start.AddMinutes(45), 0.00001m, MeasurementStatus.Measured);
        });

        var total = (await Repository().GetHourlyTotalsAsync(maLo, day, day, CancellationToken.None)).ShouldHaveSingleItem();

        total.EnergyKwh.ShouldBe(1m);
        total.Start.ShouldBe(new DateTimeOffset(2026, 6, 10, 10, 0, 0, Cest));
    }

    [SkippableFact]
    public async Task Only_the_energy_series_of_the_market_location_within_the_period_counts()
    {
        const string maLo = "50000000005";
        const string otherMaLo = "60000000004";
        var day = new DateOnly(2026, 6, 10);
        await SaveSeriesAsync(maLo, s =>
        {
            FillDays(s, day.AddDays(-1), day.AddDays(1), 0.1m);
            s.Substitute(GermanCalendar.StartOfDayUtc(day), 0.1m, ReplacementTrace.LinearInterpolation(0.1m, 0.1m));
            s.Substitute(GermanCalendar.StartOfDayUtc(day).AddMinutes(15), 0m, ReplacementTrace.ZeroFallback());
        }, alsoRecordOtherDirection: true);
        await SaveSeriesAsync(otherMaLo, s => FillDays(s, day, day, 1m));

        var total = (await Repository().GetDailyTotalsAsync(maLo, day, day, CancellationToken.None)).ShouldHaveSingleItem();

        total.EnergyKwh.ShouldBe(9.5m);
        total.Intervals.ShouldBe(96);
        total.MeasuredIntervals.ShouldBe(94);
    }

    [SkippableFact]
    public async Task Unknown_market_location_has_no_totals()
    {
        var day = new DateOnly(2026, 6, 10);

        var totals = await Repository().GetDailyTotalsAsync("70000000003", day, day, CancellationToken.None);

        totals.ShouldBeEmpty();
    }

    public void Dispose()
    {
        _scope?.Dispose();
        _services?.Dispose();
    }

    private static void FillDays(MeasurementSeries series, DateOnly firstDay, DateOnly lastDay, decimal value)
    {
        var end = GermanCalendar.StartOfDayUtc(lastDay.AddDays(1));
        for (var start = GermanCalendar.StartOfDayUtc(firstDay); start < end; start += MeasurementSeries.IntervalLength)
        {
            series.AddValue(start, value, MeasurementStatus.Measured);
        }
    }

    // Resolved through the real registration; built lazily because the base constructor may skip the test first.
    private IAggregationRepository Repository()
    {
        _services ??= new ServiceCollection().AddInfrastructure(Database.ConnectionString).BuildServiceProvider();
        _scope ??= _services.CreateScope();
        return _scope.ServiceProvider.GetRequiredService<IAggregationRepository>();
    }

    /// <param name="alsoRecordOtherDirection">Adds a feed-in series with large values that must not be counted.</param>
    private async Task SaveSeriesAsync(string maLoId, Action<MeasurementSeries> addValues, bool alsoRecordOtherDirection = false)
    {
        var marketLocation = new MarketLocation(maLoId, EnergyDirection.Consumption);
        var meterLocation = marketLocation.AddMeterLocation($"DE00012345678900000000000{maLoId[..8]}");
        addValues(meterLocation.AddSeries(MeasurementSeries.ConsumedActiveEnergy));
        if (alsoRecordOtherDirection)
        {
            FillDays(meterLocation.AddSeries(MeasurementSeries.FedInActiveEnergy), new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 10), 5m);
        }

        await using var context = CreateDbContext();
        context.MarketLocations.Add(marketLocation);
        await context.SaveChangesAsync();
    }
}
