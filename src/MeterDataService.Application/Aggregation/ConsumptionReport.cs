using MeterDataService.Domain;

namespace MeterDataService.Application.Aggregation;

/// <summary>
/// Energy of a market location over a period of German calendar days (Energiemenge im Abrechnungszeitraum): the
/// total, the totals per bucket, and how complete and how measured the data behind them is.
/// </summary>
/// <param name="MarketLocationId">11-digit MaLo-ID.</param>
/// <param name="Direction">Consumption or generation; tells whether the energy was drawn or fed in.</param>
/// <param name="From">First German calendar day of the period (inclusive).</param>
/// <param name="To">Last German calendar day of the period (inclusive).</param>
/// <param name="Granularity">Length of the buckets in <paramref name="Totals"/>.</param>
/// <param name="TotalEnergyKwh">Sum over the whole period, in kWh.</param>
/// <param name="Intervals">15-minute values stored in the period.</param>
/// <param name="ExpectedIntervals">15-minute intervals the period has: 96 per day, 92 / 100 on clock-change days.</param>
/// <param name="MeasuredIntervals">Stored values with status <c>Measured</c>.</param>
/// <param name="CompletenessPercent">Stored values as a share of expected ones, in percent (2 decimals).</param>
/// <param name="MeasuredPercent">Measured values as a share of expected ones, in percent (2 decimals).</param>
/// <param name="Totals">Totals per bucket. Buckets without values are left out; edge buckets cover only the period's days.</param>
public sealed record ConsumptionReport(
    string MarketLocationId,
    EnergyDirection Direction,
    DateOnly From,
    DateOnly To,
    AggregationGranularity Granularity,
    decimal TotalEnergyKwh,
    int Intervals,
    int ExpectedIntervals,
    int MeasuredIntervals,
    decimal CompletenessPercent,
    decimal MeasuredPercent,
    IReadOnlyList<EnergyTotal> Totals)
{
    public static ConsumptionReport Create(
        string marketLocationId,
        EnergyDirection direction,
        DateOnly from,
        DateOnly to,
        AggregationGranularity granularity,
        IReadOnlyList<EnergyTotal> totals)
    {
        ArgumentNullException.ThrowIfNull(totals);

        var expected = 0;
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            expected += GermanCalendar.IntervalsIn(day);
        }

        var intervals = totals.Sum(t => t.Intervals);
        var measured = totals.Sum(t => t.MeasuredIntervals);

        return new ConsumptionReport(
            marketLocationId,
            direction,
            from,
            to,
            granularity,
            totals.Sum(t => t.EnergyKwh),
            intervals,
            expected,
            measured,
            Percent(intervals, expected),
            Percent(measured, expected),
            totals);
    }

    private static decimal Percent(int part, int whole) =>
        decimal.Round(part * 100m / whole, 2, MidpointRounding.AwayFromZero);
}
