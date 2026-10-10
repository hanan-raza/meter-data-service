using MeterDataService.Application.Aggregation;
using MeterDataService.Application.MarketLocations;
using MeterDataService.Domain;
using Shouldly;

namespace MeterDataService.Tests.Unit.Application;

public class ConsumptionReportTests
{
    private const string MaLo = "41373559241";

    private static readonly TimeSpan Cest = TimeSpan.FromHours(2);

    [Theory]
    [InlineData(2026, 6, 10, 2026, 6, 10, 96)]
    [InlineData(2026, 3, 29, 2026, 3, 29, 92)]
    [InlineData(2026, 10, 25, 2026, 10, 25, 100)]
    [InlineData(2026, 3, 1, 2026, 3, 31, 2972)] // 31 × 96 − 4
    [InlineData(2026, 10, 1, 2026, 10, 31, 2980)] // 31 × 96 + 4
    [InlineData(2026, 1, 1, 2026, 12, 31, 35040)] // the two clock changes cancel out
    public void Expected_intervals_follow_the_clock_changes(int y1, int m1, int d1, int y2, int m2, int d2, int expected)
    {
        var report = ConsumptionReport.Create(
            MaLo, EnergyDirection.Consumption, new DateOnly(y1, m1, d1), new DateOnly(y2, m2, d2), AggregationGranularity.Day, []);

        report.ExpectedIntervals.ShouldBe(expected);
    }

    [Fact]
    public void Totals_and_shares_are_summed_over_the_buckets()
    {
        var day = new DateOnly(2026, 10, 25);
        var start = new DateTimeOffset(2026, 10, 25, 0, 0, 0, Cest);
        EnergyTotal[] totals =
        [
            new(start, start.AddHours(12), 4.33333m, 48, 47),
            new(start.AddHours(12), start.AddHours(25), 5.66667m, 49, 46),
        ];

        var report = ConsumptionReport.Create(MaLo, EnergyDirection.Consumption, day, day, AggregationGranularity.Hour, totals);

        report.TotalEnergyKwh.ShouldBe(10m);
        report.Intervals.ShouldBe(97);
        report.ExpectedIntervals.ShouldBe(100);
        report.MeasuredIntervals.ShouldBe(93);
        report.CompletenessPercent.ShouldBe(97m);
        report.MeasuredPercent.ShouldBe(93m);
        report.Totals.ShouldBe(totals);
    }

    [Fact]
    public void Shares_are_rounded_to_two_decimals_half_away_from_zero()
    {
        // 1 of 96 intervals = 1.041666…%; 3 of 96 = 3.125% → 3.13.
        var day = new DateOnly(2026, 6, 10);
        var start = new DateTimeOffset(2026, 6, 10, 0, 0, 0, Cest);

        var report = ConsumptionReport.Create(
            MaLo, EnergyDirection.Consumption, day, day, AggregationGranularity.Day, [new EnergyTotal(start, start.AddDays(1), 0.3m, 3, 1)]);

        report.CompletenessPercent.ShouldBe(3.13m);
        report.MeasuredPercent.ShouldBe(1.04m);
    }

    [Fact]
    public void A_period_without_values_reports_zero_energy_and_zero_completeness()
    {
        var day = new DateOnly(2026, 6, 10);

        var report = ConsumptionReport.Create(MaLo, EnergyDirection.Generation, day, day, AggregationGranularity.Day, []);

        report.TotalEnergyKwh.ShouldBe(0m);
        report.CompletenessPercent.ShouldBe(0m);
        report.Direction.ShouldBe(EnergyDirection.Generation);
        report.Totals.ShouldBeEmpty();
    }

    [Fact]
    public async Task Service_refuses_quarter_hours_and_returns_null_for_an_unknown_market_location()
    {
        var service = new ConsumptionQueryService(new NoMarketLocations(), new ThrowingAggregation());
        var day = new DateOnly(2026, 6, 10);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            service.GetReportAsync(MaLo, day, day, AggregationGranularity.QuarterHour, CancellationToken.None));
        (await service.GetReportAsync(MaLo, day, day, AggregationGranularity.Day, CancellationToken.None)).ShouldBeNull();
    }

    private sealed class NoMarketLocations : IMarketLocationReadRepository
    {
        public Task<IReadOnlyList<MarketLocationDetails>> ListAsync(int offset, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MarketLocationDetails>>([]);

        public Task<MarketLocationDetails?> FindAsync(string maLoId, CancellationToken cancellationToken) =>
            Task.FromResult<MarketLocationDetails?>(null);
    }

    // An unknown market location must be answered without summing anything.
    private sealed class ThrowingAggregation : IAggregationRepository
    {
        public Task<IReadOnlyList<EnergyTotal>> GetHourlyTotalsAsync(string maLoId, DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Not expected.");

        public Task<IReadOnlyList<EnergyTotal>> GetDailyTotalsAsync(string maLoId, DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Not expected.");

        public Task<IReadOnlyList<EnergyTotal>> GetMonthlyTotalsAsync(string maLoId, DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Not expected.");
    }
}
