using System.Globalization;
using MeterDataService.Domain;

namespace MeterDataService.Application.Import;

/// <summary>
/// Plausibility checks (Plausibilisierung) for imported interval values over a period of German calendar days.
/// </summary>
/// <remarks>
/// Per value: readings outside the period, duplicate intervals, negative values and spikes are rejected.
/// Per period: every interval without a reading is listed, and DST days whose interval count isn't 92 / 100
/// get their own finding, because a sender that ignores the clock change shifts every following value.
/// The engine never changes values; replacing rejected and missing ones is left to gap filling.
/// </remarks>
public sealed class ValidationEngine
{
    public const decimal DefaultSpikeFactor = 3m;

    // ±30 minutes around the value: the two intervals on either side.
    private static readonly TimeSpan SpikeWindow = TimeSpan.FromMinutes(30);

    // A median of a single neighbour is just that neighbour; too weak to reject a meter value on.
    private const int MinimumSpikeNeighbours = 2;

    /// <param name="spikeFactor">A value above this multiple of the median of its surrounding hour is a spike.</param>
    public ValidationEngine(decimal spikeFactor = DefaultSpikeFactor)
    {
        if (spikeFactor <= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(spikeFactor), spikeFactor, "Spike factor must be greater than 1.");
        }

        SpikeFactor = spikeFactor;
    }

    public decimal SpikeFactor { get; }

    /// <summary>Validates over the local days spanned by the readings themselves.</summary>
    public ValidationReport Validate(IReadOnlyList<ImportedReading> readings)
    {
        ArgumentNullException.ThrowIfNull(readings);
        if (readings.Count == 0)
        {
            return new ValidationReport([], [], []);
        }

        return Validate(
            readings,
            GermanCalendar.DayOf(readings.Min(r => r.IntervalStart)),
            GermanCalendar.DayOf(readings.Max(r => r.IntervalStart)));
    }

    /// <summary>Validates against an expected period, so gaps at its start or end are found as well.</summary>
    public ValidationReport Validate(IReadOnlyList<ImportedReading> readings, DateOnly firstDay, DateOnly lastDay)
    {
        ArgumentNullException.ThrowIfNull(readings);
        if (lastDay < firstDay)
        {
            throw new ArgumentException($"Last day {lastDay:O} is before first day {firstDay:O}.", nameof(lastDay));
        }

        var periodStart = GermanCalendar.StartOfDayUtc(firstDay);
        var periodEnd = GermanCalendar.StartOfDayUtc(lastDay.AddDays(1));

        var results = new ValidationResult[readings.Count];
        var present = new HashSet<DateTimeOffset>();
        var plausible = new Dictionary<DateTimeOffset, decimal>();

        for (var i = 0; i < readings.Count; i++)
        {
            var reading = readings[i];
            var start = reading.IntervalStart;
            if (start < periodStart || start >= periodEnd)
            {
                results[i] = ValidationResult.Rejected(
                    ValidationRule.OutsidePeriod,
                    Format($"Interval {start:O} is outside the import period {firstDay:yyyy-MM-dd} – {lastDay:yyyy-MM-dd}."));
            }
            else if (!present.Add(start))
            {
                results[i] = ValidationResult.Rejected(
                    ValidationRule.DuplicateInterval,
                    Format($"Interval {start:O} was already delivered; the first value is kept."));
            }
            else if (reading.Value < 0)
            {
                results[i] = ValidationResult.Rejected(
                    ValidationRule.NegativeValue,
                    Format($"Value {reading.Value} kWh is negative; energy per interval and direction can't be below zero."));
            }
            else
            {
                results[i] = ValidationResult.Ok;
                plausible[start] = reading.Value;
            }
        }

        // Separate pass: every neighbour must be known before a value is judged against them.
        for (var i = 0; i < readings.Count; i++)
        {
            if (results[i].Outcome == ValidationOutcome.Ok)
            {
                results[i] = CheckSpike(readings[i], plausible);
            }
        }

        var missing = FindMissingIntervals(periodStart, periodEnd, present);
        var findings = MissingIntervalFindings(missing)
            .Concat(DstIntervalCountFindings(firstDay, lastDay, present))
            .OrderBy(f => f.From)
            .ToList();

        return new ValidationReport(
            readings.Select((reading, i) => new ValidatedReading(reading, results[i])).ToList(),
            missing,
            findings);
    }

    private ValidationResult CheckSpike(ImportedReading reading, Dictionary<DateTimeOffset, decimal> plausible)
    {
        var neighbours = new List<decimal>(4);
        for (var offset = -SpikeWindow; offset <= SpikeWindow; offset += MeasurementSeries.IntervalLength)
        {
            if (offset != TimeSpan.Zero && plausible.TryGetValue(reading.IntervalStart + offset, out var neighbour))
            {
                neighbours.Add(neighbour);
            }
        }

        if (neighbours.Count < MinimumSpikeNeighbours)
        {
            return ValidationResult.Ok;
        }

        // A zero median (e.g. PV at night) gives no scale to compare against; absolute limits need meter master data.
        var median = Median(neighbours);
        if (median > 0 && reading.Value > SpikeFactor * median)
        {
            return ValidationResult.Rejected(
                ValidationRule.Spike,
                Format($"Value {reading.Value} kWh is more than {SpikeFactor}× the median {median} kWh of the surrounding hour."));
        }

        return ValidationResult.Ok;
    }

    private static List<DateTimeOffset> FindMissingIntervals(
        DateTimeOffset periodStart, DateTimeOffset periodEnd, HashSet<DateTimeOffset> present)
    {
        var missing = new List<DateTimeOffset>();
        for (var start = periodStart; start < periodEnd; start += MeasurementSeries.IntervalLength)
        {
            if (!present.Contains(start))
            {
                missing.Add(start);
            }
        }

        return missing;
    }

    private static IEnumerable<ValidationFinding> MissingIntervalFindings(List<DateTimeOffset> missing)
    {
        var i = 0;
        while (i < missing.Count)
        {
            var runStart = i;
            while (i + 1 < missing.Count && missing[i + 1] == missing[i] + MeasurementSeries.IntervalLength)
            {
                i++;
            }

            var from = missing[runStart];
            var to = missing[i] + MeasurementSeries.IntervalLength;
            var count = i - runStart + 1;
            yield return new ValidationFinding(
                ValidationRule.MissingInterval,
                ValidationOutcome.Warning,
                from,
                to,
                Format($"{count} missing interval(s) from {from:O} to {to:O}."));
            i++;
        }
    }

    private static IEnumerable<ValidationFinding> DstIntervalCountFindings(
        DateOnly firstDay, DateOnly lastDay, HashSet<DateTimeOffset> present)
    {
        for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
        {
            var expected = GermanCalendar.IntervalsIn(day);
            if (expected == GermanCalendar.QuarterHoursPerNormalDay)
            {
                continue;
            }

            var from = GermanCalendar.StartOfDayUtc(day);
            var to = GermanCalendar.StartOfDayUtc(day.AddDays(1));
            var actual = present.Count(start => start >= from && start < to);
            if (actual == expected)
            {
                continue;
            }

            var hours = expected / 4;
            var hint = actual == GermanCalendar.QuarterHoursPerNormalDay
                ? " The sender probably ignored the clock change."
                : string.Empty;
            yield return new ValidationFinding(
                ValidationRule.DstIntervalCount,
                ValidationOutcome.Warning,
                from,
                to,
                Format($"{hours}-hour day {day:yyyy-MM-dd} has {actual} intervals, expected {expected}.{hint}"));
        }
    }

    private static decimal Median(List<decimal> values)
    {
        values.Sort();
        var mid = values.Count / 2;
        return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2;
    }

    private static string Format(FormattableString message) => message.ToString(CultureInfo.InvariantCulture);
}
