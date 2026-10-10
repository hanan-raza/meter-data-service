namespace MeterDataService.Application.Aggregation;

/// <summary>
/// Rules for a requested period of German calendar days (Abrechnungs- / Auswertungszeitraum). The longest period
/// grows with the bucket length, so one response holds at most a few thousand points.
/// </summary>
public static class ReportingPeriod
{
    /// <summary>Longest period in days for the granularity: ~3,000 quarter hours, ~2,200 hours, or three years.</summary>
    public static int MaxDays(AggregationGranularity granularity) => granularity switch
    {
        AggregationGranularity.QuarterHour => 31,
        AggregationGranularity.Hour => 92,
        AggregationGranularity.Day or AggregationGranularity.Month => 3 * 366,
        _ => throw new ArgumentOutOfRangeException(nameof(granularity), granularity, "Unknown granularity."),
    };

    /// <summary>Why the period can't be served, or <c>null</c> if it can. Both days are inclusive.</summary>
    public static string? Validate(DateOnly from, DateOnly to, AggregationGranularity granularity)
    {
        if (to < from)
        {
            return $"'to' ({to:yyyy-MM-dd}) is before 'from' ({from:yyyy-MM-dd}).";
        }

        var days = to.DayNumber - from.DayNumber + 1;
        var maxDays = MaxDays(granularity);
        return days > maxDays
            ? $"{days} days requested; at most {maxDays} days are allowed at granularity {granularity}."
            : null;
    }
}
