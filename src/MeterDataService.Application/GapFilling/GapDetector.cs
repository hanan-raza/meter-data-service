using MeterDataService.Domain;

namespace MeterDataService.Application.GapFilling;

/// <summary>
/// Finds gaps (Messlücken) in a measurement series over a period: intervals with no value, and intervals
/// whose value was rejected by plausibility checks. Both need a substitute value (Ersatzwert) before billing.
/// </summary>
public static class GapDetector
{
    /// <summary>Finds gaps over whole German calendar days, so DST days get their 92 or 100 intervals.</summary>
    public static IReadOnlyList<Gap> FindGaps(
        MeasurementSeries series,
        DateOnly firstDay,
        DateOnly lastDay,
        IReadOnlySet<DateTimeOffset>? rejectedIntervals = null) =>
        FindGaps(
            series,
            GermanCalendar.StartOfDayUtc(firstDay),
            GermanCalendar.StartOfDayUtc(lastDay.AddDays(1)),
            rejectedIntervals);

    /// <param name="series">Series to inspect.</param>
    /// <param name="periodStart">Inclusive start of the period; must be on a quarter-hour.</param>
    /// <param name="periodEnd">Exclusive end of the period; must be on a quarter-hour.</param>
    /// <param name="rejectedIntervals">
    /// UTC starts of intervals whose value must not be used, even if the series contains one.
    /// Also respected for anchors, which may lie just outside the period.
    /// </param>
    public static IReadOnlyList<Gap> FindGaps(
        MeasurementSeries series,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        IReadOnlySet<DateTimeOffset>? rejectedIntervals = null)
    {
        ArgumentNullException.ThrowIfNull(series);
        EnsureAligned(periodStart, nameof(periodStart));
        EnsureAligned(periodEnd, nameof(periodEnd));
        if (periodEnd <= periodStart)
        {
            throw new ArgumentException($"Period end {periodEnd:O} must be after its start {periodStart:O}.", nameof(periodEnd));
        }

        var rejected = rejectedIntervals ?? new HashSet<DateTimeOffset>();
        var usable = new Dictionary<DateTimeOffset, decimal>();
        foreach (var value in series.Values)
        {
            if (!rejected.Contains(value.IntervalStart))
            {
                // A series shouldn't hold two values for one interval (unique index); if it does, the first wins.
                usable.TryAdd(value.IntervalStart, value.Value);
            }
        }

        var gaps = new List<Gap>();
        DateTimeOffset? gapStart = null;
        var step = MeasurementSeries.IntervalLength;
        for (var start = periodStart.ToUniversalTime(); start < periodEnd; start += step)
        {
            var isUsable = usable.ContainsKey(start);
            if (!isUsable && gapStart is null)
            {
                gapStart = start;
            }
            else if (isUsable && gapStart is { } from)
            {
                gaps.Add(NewGap(from, start, usable));
                gapStart = null;
            }
        }

        if (gapStart is { } openFrom)
        {
            gaps.Add(NewGap(openFrom, periodEnd.ToUniversalTime(), usable));
        }

        return gaps;
    }

    private static Gap NewGap(DateTimeOffset from, DateTimeOffset to, Dictionary<DateTimeOffset, decimal> usable) =>
        new(
            from,
            to,
            usable.TryGetValue(from - MeasurementSeries.IntervalLength, out var before) ? before : null,
            usable.TryGetValue(to, out var after) ? after : null);

    private static void EnsureAligned(DateTimeOffset instant, string paramName)
    {
        if (instant.UtcTicks % MeasurementSeries.IntervalLength.Ticks != 0)
        {
            throw new ArgumentException($"{instant:O} is not on a quarter-hour boundary.", paramName);
        }
    }
}
