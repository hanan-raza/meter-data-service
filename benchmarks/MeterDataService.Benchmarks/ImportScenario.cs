using System.Globalization;
using System.Text;
using MeterDataService.Application.GapFilling;
using MeterDataService.Application.Synthetic;
using MeterDataService.Domain;

namespace MeterDataService.Benchmarks;

/// <summary>
/// One week of household values (672 quarter hours) as a delivered CSV, plus the two weeks already stored
/// before it that similar-day filling looks back on. Deterministic, so runs are comparable.
/// </summary>
/// <remarks>
/// The week has the defects a real delivery brings, so every pipeline step does actual work: two short gaps
/// for interpolation, one three-hour gap for similar-day filling and one spike for validation to reject.
/// A normal week without a DST switch, so the interval count is exactly 7 × 96.
/// </remarks>
internal sealed class ImportScenario
{
    public const string MaLoId = "41373559241";
    public const int IntervalsInWeek = 7 * GermanCalendar.QuarterHoursPerNormalDay;

    // Monday to Sunday.
    public static readonly DateOnly FirstDay = new(2026, 9, 7);
    public static readonly DateOnly LastDay = new(2026, 9, 13);

    // Interval indexes within the week: (first, count). Monday 07:30, Tuesday 10:00, Wednesday 12:00–15:00.
    private static readonly (int First, int Count)[] Gaps = [(30, 2), (96 + 40, 4), (2 * 96 + 48, 12)];
    private const int SpikeIndex = 5 * 96 + 76;
    private const decimal SpikeFactor = 6m;

    private ImportScenario(string csv, (DateTimeOffset Start, decimal Value)[] history, int rows)
    {
        Csv = csv;
        History = history;
        Rows = rows;
    }

    /// <summary>The delivered file: header plus one row per delivered interval, timestamps with offset.</summary>
    public string Csv { get; }

    /// <summary>Measured values already stored for the <see cref="SimilarDayFiller.LookbackDays"/> before the week.</summary>
    public (DateTimeOffset Start, decimal Value)[] History { get; }

    /// <summary>Data rows in <see cref="Csv"/>: the week minus the gaps.</summary>
    public int Rows { get; }

    /// <summary>Intervals missing from the file, all of which gap filling must replace.</summary>
    public static int MissingIntervals => Gaps.Sum(g => g.Count);

    public static ImportScenario Create()
    {
        var generator = new SyntheticProfileGenerator(new SyntheticProfileOptions { GapProbability = 0, SpikeProbability = 0, Seed = 13 });
        var all = generator
            .GenerateHousehold(Guid.Empty, FirstDay.AddDays(-SimilarDayFiller.LookbackDays), LastDay)
            .Select(v => (v.IntervalStart, v.Value))
            .ToArray();

        var weekStart = GermanCalendar.StartOfDayUtc(FirstDay);
        var history = all.Where(v => v.IntervalStart < weekStart).ToArray();
        var week = all.Where(v => v.IntervalStart >= weekStart).ToArray();
        if (week.Length != IntervalsInWeek)
        {
            throw new InvalidOperationException($"Expected {IntervalsInWeek} intervals in the week, got {week.Length}.");
        }

        var csv = new StringBuilder("timestamp,value,status\n");
        var rows = 0;
        for (var i = 0; i < week.Length; i++)
        {
            if (Gaps.Any(g => i >= g.First && i < g.First + g.Count))
            {
                continue;
            }

            var (start, value) = week[i];
            if (i == SpikeIndex)
            {
                value *= SpikeFactor;
            }

            csv.Append(CultureInfo.InvariantCulture, $"{GermanCalendar.ToGermanTime(start):yyyy-MM-dd'T'HH:mm:sszzz},{value},Measured\n");
            rows++;
        }

        return new ImportScenario(csv.ToString(), history, rows);
    }

    /// <summary>A fresh series holding only the history, as the repository would load it for this import.</summary>
    public MeasurementSeries NewSeriesWithHistory()
    {
        var series = new MarketLocation(MaLoId, EnergyDirection.Consumption)
            .AddMeterLocation("DE0001234567890000000000000000001")
            .AddSeries(MeasurementSeries.ConsumedActiveEnergy);
        foreach (var (start, value) in History)
        {
            series.AddValue(start, value, MeasurementStatus.Measured);
        }

        return series;
    }
}
