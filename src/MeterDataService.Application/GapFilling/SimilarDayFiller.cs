using MeterDataService.Domain;

namespace MeterDataService.Application.GapFilling;

/// <summary>
/// Fills long gaps with the same local time slice of a recent similar day (Vergleichstagverfahren).
/// Load follows the daily routine, so last Wednesday's 12:00–15:00 is a far better guess for this
/// Wednesday's than a straight line would be.
/// </summary>
/// <remarks>
/// Day types are workday (Mon–Fri), Saturday and Sunday. Candidates are the same weekday first, then the
/// other days of the same type, most recent first, all within <see cref="LookbackDays"/>. A candidate is
/// taken only if every interval of the slice holds a measured, non-rejected value: a substitute is never
/// derived from another substitute. A gap crossing midnight is split per day, each part with its own source.
/// Without any candidate, the slice is set to 0 and marked <see cref="MeasurementStatus.Estimated"/>.
/// </remarks>
public static class SimilarDayFiller
{
    /// <summary>How many days back a similar day may lie.</summary>
    public const int LookbackDays = 14;

    private enum DayType
    {
        Workday,
        Saturday,
        Sunday,
    }

    /// <param name="series">Series to fill; must already hold the lookback days before the gaps.</param>
    /// <param name="gaps">Gaps to fill, typically what linear interpolation left over.</param>
    /// <param name="rejectedIntervals">UTC starts of intervals whose value must not be used as a source.</param>
    /// <returns>Every written value; <see cref="GapFillResult.Unfilled"/> is always empty, as this is the last step.</returns>
    public static GapFillResult Fill(
        MeasurementSeries series,
        IReadOnlyList<Gap> gaps,
        IReadOnlySet<DateTimeOffset>? rejectedIntervals = null)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(gaps);

        var rejected = rejectedIntervals ?? new HashSet<DateTimeOffset>();
        var measured = new Dictionary<DateTimeOffset, decimal>();
        var senderEstimates = new HashSet<DateTimeOffset>();
        foreach (var value in series.Values)
        {
            if (rejected.Contains(value.IntervalStart))
            {
                continue;
            }

            if (value.Status == MeasurementStatus.Measured)
            {
                measured.TryAdd(value.IntervalStart, value.Value);
            }
            else if (value.Status == MeasurementStatus.Estimated && value.ReplacedBy is null)
            {
                senderEstimates.Add(value.IntervalStart);
            }
        }

        var written = new List<MeasurementValue>();
        foreach (var gap in gaps)
        {
            foreach (var slice in gap.IntervalStarts().GroupBy(GermanCalendar.DayOf))
            {
                var starts = slice.ToList();
                if (FindSource(slice.Key, starts, measured) is { } source)
                {
                    var trace = ReplacementTrace.SimilarDay(source.Day);
                    for (var i = 0; i < starts.Count; i++)
                    {
                        written.Add(series.Substitute(starts[i], source.Values[i], trace));
                    }

                    continue;
                }

                // A sender's own estimate is still better than a zero placeholder.
                foreach (var start in starts.Where(s => !senderEstimates.Contains(s)))
                {
                    written.Add(series.Substitute(start, 0m, ReplacementTrace.ZeroFallback()));
                }
            }
        }

        return new GapFillResult(written, []);
    }

    private static (DateOnly Day, decimal[] Values)? FindSource(
        DateOnly day,
        List<DateTimeOffset> starts,
        Dictionary<DateTimeOffset, decimal> measured)
    {
        foreach (var candidate in Candidates(day))
        {
            var values = new decimal[starts.Count];
            var complete = true;
            for (var i = 0; i < starts.Count && complete; i++)
            {
                complete = SameLocalTimeOn(candidate, starts[i]) is { } source
                    && measured.TryGetValue(source, out values[i]);
            }

            if (complete)
            {
                return (candidate, values);
            }
        }

        return null;
    }

    // OrderBy is stable, so within each group the most recent day stays first.
    private static IEnumerable<DateOnly> Candidates(DateOnly day) =>
        Enumerable.Range(1, LookbackDays)
            .Select(daysBack => day.AddDays(-daysBack))
            .Where(candidate => TypeOf(candidate) == TypeOf(day))
            .OrderBy(candidate => candidate.DayOfWeek == day.DayOfWeek ? 0 : 1);

    private static DayType TypeOf(DateOnly day) => day.DayOfWeek switch
    {
        DayOfWeek.Saturday => DayType.Saturday,
        DayOfWeek.Sunday => DayType.Sunday,
        _ => DayType.Workday,
    };

    /// <summary>
    /// UTC start of the interval on <paramref name="sourceDay"/> with the same local wall-clock time, or null
    /// if that time doesn't exist there (02:00–03:00 on the spring-forward day).
    /// </summary>
    /// <remarks>
    /// Both passes of the repeated hour on a fall-back target day map to the same source interval. If the
    /// source is the fall-back day, the pass with the target's UTC offset is used.
    /// </remarks>
    private static DateTimeOffset? SameLocalTimeOn(DateOnly sourceDay, DateTimeOffset intervalStart)
    {
        var zone = GermanCalendar.TimeZone;
        var local = TimeZoneInfo.ConvertTime(intervalStart, zone);
        var wallClock = sourceDay.ToDateTime(TimeOnly.FromTimeSpan(local.TimeOfDay));
        if (zone.IsInvalidTime(wallClock))
        {
            return null;
        }

        var offset = zone.IsAmbiguousTime(wallClock)
            ? zone.GetAmbiguousTimeOffsets(wallClock).OrderBy(o => o == local.Offset ? 0 : 1).First()
            : zone.GetUtcOffset(wallClock);
        return new DateTimeOffset(wallClock, offset).ToUniversalTime();
    }
}
