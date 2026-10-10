using MeterDataService.Application.Aggregation;
using MeterDataService.Application.MarketLocations;
using MeterDataService.Application.Measurements;
using MeterDataService.Domain;
using Shouldly;

namespace MeterDataService.Tests.Unit.Application;

public class MeasurementQueryServiceTests
{
    private const string MaLo = "41373559241";

    private static readonly TimeSpan Cet = TimeSpan.FromHours(1);
    private static readonly TimeSpan Cest = TimeSpan.FromHours(2);
    private static readonly DateOnly FallBackDay = new(2026, 10, 25);

    private readonly FakeMarketLocations _marketLocations = new();
    private readonly FakeMeasurements _measurements = new();
    private readonly FakeAggregation _aggregation = new();

    public MeasurementQueryServiceTests()
    {
        _marketLocations.Known = new MarketLocationDetails(MaLo, EnergyDirection.Consumption, MeasurementSeries.ConsumedActiveEnergy, []);
    }

    [Fact]
    public async Task Unknown_market_location_gives_null_without_reading_values()
    {
        _marketLocations.Known = null;

        var series = await Service().GetSeriesAsync(MaLo, FallBackDay, FallBackDay, AggregationGranularity.QuarterHour, CancellationToken.None);

        series.ShouldBeNull();
        _measurements.Calls.ShouldBe(0);
        _aggregation.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Quarter_hour_points_are_the_stored_values_in_german_time_with_status_and_trace()
    {
        var series = new MarketLocation(MaLo, EnergyDirection.Consumption)
            .AddMeterLocation("DE0001234567890000000000000041373")
            .AddSeries(MeasurementSeries.ConsumedActiveEnergy);

        // 00:45 UTC is the first pass of local 02:45 (CEST), 01:00 UTC is the second pass of local 02:00 (CET).
        var firstPass = new DateTimeOffset(2026, 10, 25, 0, 45, 0, TimeSpan.Zero);
        var secondPass = firstPass.AddMinutes(15);
        _measurements.Values =
        [
            series.AddValue(firstPass, 0.4m, MeasurementStatus.Measured),
            series.Substitute(secondPass, 0.45m, ReplacementTrace.LinearInterpolation(0.4m, 0.5m)),
        ];

        var view = (await Service().GetSeriesAsync(MaLo, FallBackDay, FallBackDay, AggregationGranularity.QuarterHour, CancellationToken.None)).ShouldNotBeNull();

        view.ObisCode.ShouldBe(MeasurementSeries.ConsumedActiveEnergy);
        view.Granularity.ShouldBe(AggregationGranularity.QuarterHour);
        view.Points.Count.ShouldBe(2);

        var measured = view.Points[0];
        measured.Start.ShouldBe(firstPass);
        measured.Start.Offset.ShouldBe(Cest);
        measured.End.Offset.ShouldBe(Cet);
        (measured.End - measured.Start).ShouldBe(TimeSpan.FromMinutes(15));
        measured.Status.ShouldBe(MeasurementStatus.Measured);
        measured.MeasuredIntervals.ShouldBe(1);
        measured.Substitution.ShouldBeNull();

        var replaced = view.Points[1];
        replaced.Start.ShouldBe(new DateTimeOffset(2026, 10, 25, 2, 0, 0, Cet));
        replaced.EnergyKwh.ShouldBe(0.45m);
        replaced.Intervals.ShouldBe(1);
        replaced.MeasuredIntervals.ShouldBe(0);
        replaced.Status.ShouldBe(MeasurementStatus.Replaced);
        replaced.Substitution.ShouldBe(new Substitution(ReplacementMethod.LinearInterpolation, 0.4m, 0.5m, null));
        _aggregation.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(AggregationGranularity.Hour)]
    [InlineData(AggregationGranularity.Day)]
    [InlineData(AggregationGranularity.Month)]
    public async Task Coarser_points_are_database_sums_without_status(AggregationGranularity granularity)
    {
        var start = new DateTimeOffset(2026, 10, 25, 0, 0, 0, Cest);
        _aggregation.Totals = [new EnergyTotal(start, start.AddHours(25), 10m, 100, 98)];

        var view = (await Service().GetSeriesAsync(MaLo, FallBackDay, FallBackDay, granularity, CancellationToken.None)).ShouldNotBeNull();

        _aggregation.Calls.ShouldBe([granularity]);
        _measurements.Calls.ShouldBe(0);
        view.Points.ShouldHaveSingleItem().ShouldBe(new MeasurementPoint(start, start.AddHours(25), 10m, 100, 98));
    }

    [Fact]
    public async Task A_period_over_the_limit_is_refused()
    {
        await Should.ThrowAsync<ArgumentException>(() => Service().GetSeriesAsync(
            MaLo, FallBackDay, FallBackDay.AddDays(31), AggregationGranularity.QuarterHour, CancellationToken.None));
    }

    private MeasurementQueryService Service() => new(_marketLocations, _measurements, _aggregation);

    private sealed class FakeMarketLocations : IMarketLocationReadRepository
    {
        public MarketLocationDetails? Known { get; set; }

        public Task<IReadOnlyList<MarketLocationDetails>> ListAsync(int offset, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MarketLocationDetails>>(Known is null ? [] : [Known]);

        public Task<MarketLocationDetails?> FindAsync(string maLoId, CancellationToken cancellationToken) =>
            Task.FromResult(Known?.MaLoId == maLoId ? Known : null);
    }

    private sealed class FakeMeasurements : IMeasurementReadRepository
    {
        public IReadOnlyList<MeasurementValue> Values { get; set; } = [];

        public int Calls { get; private set; }

        public Task<IReadOnlyList<MeasurementValue>> GetEnergyValuesAsync(string maLoId, DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Values);
        }
    }

    private sealed class FakeAggregation : IAggregationRepository
    {
        public IReadOnlyList<EnergyTotal> Totals { get; set; } = [];

        public List<AggregationGranularity> Calls { get; } = [];

        public Task<IReadOnlyList<EnergyTotal>> GetHourlyTotalsAsync(string maLoId, DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken) =>
            Record(AggregationGranularity.Hour);

        public Task<IReadOnlyList<EnergyTotal>> GetDailyTotalsAsync(string maLoId, DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken) =>
            Record(AggregationGranularity.Day);

        public Task<IReadOnlyList<EnergyTotal>> GetMonthlyTotalsAsync(string maLoId, DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken) =>
            Record(AggregationGranularity.Month);

        private Task<IReadOnlyList<EnergyTotal>> Record(AggregationGranularity granularity)
        {
            Calls.Add(granularity);
            return Task.FromResult(Totals);
        }
    }
}
