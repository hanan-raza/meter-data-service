using System.Net;
using System.Net.Http.Json;
using MeterDataService.Application.Aggregation;
using MeterDataService.Application.Measurements;
using MeterDataService.Domain;
using MeterDataService.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace MeterDataService.Tests.Integration.Api;

/// <summary>
/// One market location with every interval of the fall-back day (100 values of 0.1 kWh), of which 02:00 CET was
/// interpolated and 10:00 copied from a similar day. Seeded once per class; every test only reads.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MeasurementEndpointTests(PostgreSqlFixture database) : ApiTestBase(database), IAsyncLifetime
{
    private const string MaLo = "50000000005";

    private static readonly TimeSpan Cet = TimeSpan.FromHours(1);
    private static readonly TimeSpan Cest = TimeSpan.FromHours(2);
    private static readonly DateOnly FallBackDay = new(2026, 10, 25);
    private static readonly DateOnly SimilarDay = new(2026, 10, 18);

    // 02:00 CET, the second pass of the repeated hour.
    private static readonly DateTimeOffset Interpolated = new(2026, 10, 25, 2, 0, 0, Cet);
    private static readonly DateTimeOffset CopiedFromSimilarDay = new(2026, 10, 25, 10, 0, 0, Cet);

    public async Task InitializeAsync()
    {
        await using var context = CreateDbContext();
        if (await context.MarketLocations.AnyAsync(m => m.MaLoId == MaLo))
        {
            return;
        }

        var marketLocation = new MarketLocation(MaLo, EnergyDirection.Consumption);
        var series = marketLocation.AddMeterLocation("DE0001234567890000000000050000000").AddSeries(MeasurementSeries.ConsumedActiveEnergy);
        var end = GermanCalendar.StartOfDayUtc(FallBackDay.AddDays(1));
        for (var start = GermanCalendar.StartOfDayUtc(FallBackDay); start < end; start += MeasurementSeries.IntervalLength)
        {
            series.AddValue(start, 0.1m, MeasurementStatus.Measured);
        }

        series.Substitute(Interpolated, 0.1m, ReplacementTrace.LinearInterpolation(0.1m, 0.1m));
        series.Substitute(CopiedFromSimilarDay, 0.1m, ReplacementTrace.SimilarDay(SimilarDay));
        context.MarketLocations.Add(marketLocation);
        await context.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [SkippableFact]
    public async Task Quarter_hour_series_of_the_fall_back_day_has_100_values_with_status_and_trace()
    {
        using var client = Factory.CreateClient();

        var series = await GetSeriesAsync(client, $"?from={FallBackDay:yyyy-MM-dd}&to={FallBackDay:yyyy-MM-dd}");

        series.MarketLocationId.ShouldBe(MaLo);
        series.ObisCode.ShouldBe(MeasurementSeries.ConsumedActiveEnergy);
        series.Granularity.ShouldBe(AggregationGranularity.QuarterHour);
        series.Points.Count.ShouldBe(100);
        series.Points[0].Start.ShouldBe(new DateTimeOffset(2026, 10, 25, 0, 0, 0, Cest));
        series.Points[0].Start.Offset.ShouldBe(Cest);
        series.Points[^1].End.ShouldBe(new DateTimeOffset(2026, 10, 26, 0, 0, 0, Cet));
        series.Points[^1].End.Offset.ShouldBe(Cet);

        var interpolated = series.Points.Single(p => p.Start == Interpolated);
        interpolated.Start.Offset.ShouldBe(Cet);
        interpolated.Status.ShouldBe(MeasurementStatus.Replaced);
        interpolated.Substitution.ShouldBe(new Substitution(ReplacementMethod.LinearInterpolation, 0.1m, 0.1m, null));

        var copied = series.Points.Single(p => p.Start == CopiedFromSimilarDay);
        copied.Substitution.ShouldBe(new Substitution(ReplacementMethod.SimilarDay, null, null, SimilarDay));

        series.Points.Count(p => p.Status == MeasurementStatus.Measured).ShouldBe(98);
    }

    [SkippableFact]
    public async Task Hourly_series_has_25_hours_with_both_passes_of_02_00()
    {
        using var client = Factory.CreateClient();

        var series = await GetSeriesAsync(client, $"?from={FallBackDay:yyyy-MM-dd}&to={FallBackDay:yyyy-MM-dd}&granularity=Hour");

        series.Points.Count.ShouldBe(25);
        series.Points.ShouldAllBe(p => p.EnergyKwh == 0.4m && p.Intervals == 4 && p.Status == null);
        series.Points.Where(p => p.Start.Hour == 2).Select(p => p.Start.Offset).ShouldBe([Cest, Cet]);
        series.Points.Single(p => p.Start == Interpolated).MeasuredIntervals.ShouldBe(3);
    }

    [SkippableTheory]
    [InlineData("Day")]
    [InlineData("month")]
    public async Task Daily_and_monthly_series_sum_the_25_hour_day(string granularity)
    {
        using var client = Factory.CreateClient();

        var series = await GetSeriesAsync(client, $"?from={FallBackDay:yyyy-MM-dd}&to={FallBackDay:yyyy-MM-dd}&granularity={granularity}");

        var point = series.Points.ShouldHaveSingleItem();
        point.EnergyKwh.ShouldBe(10.0m);
        point.Intervals.ShouldBe(100);
        point.MeasuredIntervals.ShouldBe(98);
    }

    [SkippableFact]
    public async Task A_period_without_values_gives_an_empty_series_not_zeros()
    {
        using var client = Factory.CreateClient();

        var series = await GetSeriesAsync(client, "?from=2026-06-01&to=2026-06-30&granularity=Day");

        series.Points.ShouldBeEmpty();
    }

    [SkippableTheory]
    [InlineData("41373559242", "?from=2026-10-25&to=2026-10-25", "maLoId")] // wrong check digit
    [InlineData(MaLo, "?to=2026-10-25", "from")]
    [InlineData(MaLo, "?from=2026-10-25", "to")]
    [InlineData(MaLo, "?from=2026-10-25&to=2026-10-24", "before")]
    [InlineData(MaLo, "?from=2026-10-01&to=2026-11-01", "at most 31 days")]
    [InlineData(MaLo, "?from=25.10.2026&to=2026-10-25", "from")]
    [InlineData(MaLo, "?from=2026-10-25&to=2026-10-25&granularity=Week", "granularity")]
    public async Task Bad_requests_are_rejected_with_400_naming_the_problem(string maLo, string query, string expected)
    {
        using var client = Factory.CreateClient();

        using var response = await client.GetAsync(new Uri($"/api/market-locations/{maLo}/measurements{query}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain(expected);
    }

    [SkippableFact]
    public async Task Unknown_market_location_returns_404()
    {
        using var client = Factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/market-locations/90000000001/measurements?from=2026-10-25&to=2026-10-25", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task<MeasurementSeriesView> GetSeriesAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<MeasurementSeriesView>(new Uri($"/api/market-locations/{MaLo}/measurements{query}", UriKind.Relative), Json))
            .ShouldNotBeNull();
}
