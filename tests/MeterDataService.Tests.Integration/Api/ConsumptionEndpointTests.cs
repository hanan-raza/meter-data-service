using System.Net;
using System.Net.Http.Json;
using MeterDataService.Application.Aggregation;
using MeterDataService.Domain;
using MeterDataService.Tests.Integration.Infrastructure;
using Shouldly;

namespace MeterDataService.Tests.Integration.Api;

[Trait("Category", "Integration")]
public sealed class ConsumptionEndpointTests(PostgreSqlFixture database) : ApiTestBase(database)
{
    private static readonly TimeSpan Cet = TimeSpan.FromHours(1);
    private static readonly TimeSpan Cest = TimeSpan.FromHours(2);

    [SkippableFact]
    public async Task Daily_report_over_the_fall_back_weekend_counts_100_intervals_for_the_25_hour_day()
    {
        const string maLo = "60000000004";
        var saturday = new DateOnly(2026, 10, 24);
        var sunday = new DateOnly(2026, 10, 25);
        await SaveConsumptionAsync(maLo, saturday, sunday, 0.25m, skipFirstIntervalOf: sunday);
        using var client = Factory.CreateClient();

        var report = await GetReportAsync(client, maLo, "?from=2026-10-24&to=2026-10-25");

        report.Granularity.ShouldBe(AggregationGranularity.Day);
        report.Totals.Select(t => t.EnergyKwh).ShouldBe([24m, 24.75m]);
        report.Totals.Select(t => t.Intervals).ShouldBe([96, 99]);
        report.Totals[1].Start.ShouldBe(new DateTimeOffset(2026, 10, 25, 0, 0, 0, Cest));
        report.Totals[1].End.ShouldBe(new DateTimeOffset(2026, 10, 26, 0, 0, 0, Cet));
        report.TotalEnergyKwh.ShouldBe(48.75m);
        report.Intervals.ShouldBe(195);
        report.ExpectedIntervals.ShouldBe(196);
        report.CompletenessPercent.ShouldBe(99.49m);
        report.MeasuredPercent.ShouldBe(99.49m);
    }

    [SkippableFact]
    public async Task Monthly_report_cuts_months_at_local_midnight_and_reports_generation_as_fed_in_energy()
    {
        const string maLo = "70000000003";
        await SaveConsumptionAsync(maLo, new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 1), 0.5m, direction: EnergyDirection.Generation);
        using var client = Factory.CreateClient();

        var report = await GetReportAsync(client, maLo, "?from=2026-09-30&to=2026-10-01&granularity=Month");

        report.Direction.ShouldBe(EnergyDirection.Generation);
        report.Totals.Select(t => t.Start).ShouldBe([new DateTimeOffset(2026, 9, 1, 0, 0, 0, Cest), new DateTimeOffset(2026, 10, 1, 0, 0, 0, Cest)]);
        report.Totals.Select(t => t.EnergyKwh).ShouldBe([48m, 48m]);
        report.TotalEnergyKwh.ShouldBe(96m);
        report.CompletenessPercent.ShouldBe(100m);
    }

    [SkippableFact]
    public async Task Report_without_stored_values_is_zero_with_zero_completeness()
    {
        const string maLo = "80000000002";
        await SaveConsumptionAsync(maLo, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 10), 0.1m);
        using var client = Factory.CreateClient();

        var report = await GetReportAsync(client, maLo, "?from=2026-01-01&to=2026-01-31");

        report.TotalEnergyKwh.ShouldBe(0m);
        report.ExpectedIntervals.ShouldBe(31 * 96);
        report.CompletenessPercent.ShouldBe(0m);
        report.Totals.ShouldBeEmpty();
    }

    [SkippableTheory]
    [InlineData("41373559242", "?from=2026-10-25&to=2026-10-25", "maLoId")]
    [InlineData("41373559241", "?from=2026-10-25&to=2026-10-25&granularity=QuarterHour", "/measurements")]
    [InlineData("41373559241", "?from=2026-10-25&to=2026-10-24", "before")]
    [InlineData("41373559241", "?from=2026-01-01&to=2026-04-30&granularity=Hour", "at most 92 days")]
    [InlineData("41373559241", "?to=2026-10-25", "from")]
    public async Task Bad_requests_are_rejected_with_400_naming_the_problem(string maLo, string query, string expected)
    {
        using var client = Factory.CreateClient();

        using var response = await client.GetAsync(new Uri($"/api/market-locations/{maLo}/consumption{query}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain(expected);
    }

    [SkippableFact]
    public async Task Unknown_market_location_returns_404()
    {
        using var client = Factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/market-locations/90000000001/consumption?from=2026-10-25&to=2026-10-25", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task<ConsumptionReport> GetReportAsync(HttpClient client, string maLo, string query) =>
        (await client.GetFromJsonAsync<ConsumptionReport>(new Uri($"/api/market-locations/{maLo}/consumption{query}", UriKind.Relative), Json))
            .ShouldNotBeNull();

    private async Task SaveConsumptionAsync(
        string maLoId,
        DateOnly firstDay,
        DateOnly lastDay,
        decimal value,
        DateOnly? skipFirstIntervalOf = null,
        EnergyDirection direction = EnergyDirection.Consumption)
    {
        var marketLocation = new MarketLocation(maLoId, direction);
        var series = marketLocation.AddMeterLocation($"DE00012345678900000000000{maLoId[..8]}").AddSeries(MeasurementSeries.EnergyObisCodeFor(direction));
        var skipped = skipFirstIntervalOf is { } day ? GermanCalendar.StartOfDayUtc(day) : (DateTimeOffset?)null;
        var end = GermanCalendar.StartOfDayUtc(lastDay.AddDays(1));
        for (var start = GermanCalendar.StartOfDayUtc(firstDay); start < end; start += MeasurementSeries.IntervalLength)
        {
            if (start != skipped)
            {
                series.AddValue(start, value, MeasurementStatus.Measured);
            }
        }

        await using var context = CreateDbContext();
        context.MarketLocations.Add(marketLocation);
        await context.SaveChangesAsync();
    }
}
