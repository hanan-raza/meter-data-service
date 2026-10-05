namespace MeterDataService.Domain;

/// <summary>
/// German legal time (gesetzliche Zeit, Europe/Berlin). A market day runs from local midnight to
/// local midnight, so it lasts 23, 24 or 25 hours and holds 92, 96 or 100 quarter hours.
/// </summary>
public static class GermanCalendar
{
    public const int QuarterHoursPerNormalDay = 96;

    public static TimeZoneInfo TimeZone { get; } = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    /// <summary>UTC instant of local midnight at the start of the given day.</summary>
    public static DateTimeOffset StartOfDayUtc(DateOnly day)
    {
        // Midnight is never skipped or repeated in Germany (switches happen at 02:00/03:00), so the offset is unambiguous.
        var midnight = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(midnight, TimeZone.GetUtcOffset(midnight)).ToUniversalTime();
    }

    /// <summary>Local calendar day the instant falls on.</summary>
    public static DateOnly DayOf(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, TimeZone).DateTime);

    /// <summary>Number of 15-minute intervals in the local day: 92 on the spring-forward day, 100 on the fall-back day.</summary>
    public static int IntervalsIn(DateOnly day) =>
        (int)((StartOfDayUtc(day.AddDays(1)) - StartOfDayUtc(day)) / MeasurementSeries.IntervalLength);
}
