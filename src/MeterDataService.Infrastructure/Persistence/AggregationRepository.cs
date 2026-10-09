using MeterDataService.Application.Aggregation;
using MeterDataService.Domain;
using Microsoft.EntityFrameworkCore;

namespace MeterDataService.Infrastructure.Persistence;

/// <summary>
/// Sums in PostgreSQL: a year of one market location is ~35,000 rows, and only a few hundred totals need to leave
/// the database. <c>sum</c> over <c>numeric</c> is exact, so the totals are exact <see cref="decimal"/>s.
/// </summary>
internal sealed class AggregationRepository(MeterDataDbContext context) : IAggregationRepository
{
    private const string Utc = "UTC";

    public Task<IReadOnlyList<EnergyTotal>> GetHourlyTotalsAsync(string maLoId, DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken) =>
        GetTotalsAsync(maLoId, AggregationGranularity.Hour, firstDay, lastDay, cancellationToken);

    public Task<IReadOnlyList<EnergyTotal>> GetDailyTotalsAsync(string maLoId, DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken) =>
        GetTotalsAsync(maLoId, AggregationGranularity.Day, firstDay, lastDay, cancellationToken);

    public Task<IReadOnlyList<EnergyTotal>> GetMonthlyTotalsAsync(string maLoId, DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken) =>
        GetTotalsAsync(maLoId, AggregationGranularity.Month, firstDay, lastDay, cancellationToken);

    private async Task<IReadOnlyList<EnergyTotal>> GetTotalsAsync(
        string maLoId, AggregationGranularity granularity, DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(maLoId);
        if (lastDay < firstDay)
        {
            throw new ArgumentException($"Last day {lastDay:O} is before first day {firstDay:O}.", nameof(lastDay));
        }

        var periodStart = GermanCalendar.StartOfDayUtc(firstDay);
        var periodEnd = GermanCalendar.StartOfDayUtc(lastDay.AddDays(1));

        // Hours are truncated in UTC: German offsets are whole hours, so UTC hours are local hours, and the two
        // passes of the repeated 02:00 hour stay apart. Truncating in local time would merge them, because the
        // local 02:00 they share is ambiguous. Days and months start at local midnight, which never is.
        var (unit, zone) = granularity switch
        {
            AggregationGranularity.Hour => ("hour", Utc),
            AggregationGranularity.Day => ("day", GermanCalendar.TimeZoneId),
            AggregationGranularity.Month => ("month", GermanCalendar.TimeZoneId),
            _ => throw new ArgumentOutOfRangeException(nameof(granularity), granularity, "Unknown granularity."),
        };

        var rows = await context.Database
            .SqlQuery<TotalRow>($"""
                SELECT date_trunc({unit}, v.interval_start, {zone}) AS bucket_start,
                       sum(v.value) AS energy_kwh,
                       count(*)::int AS intervals,
                       (count(*) FILTER (WHERE v.status = {nameof(MeasurementStatus.Measured)}))::int AS measured_intervals
                FROM measurement_values v
                JOIN measurement_series s ON s.id = v.measurement_series_id
                JOIN meter_locations me ON me.id = s.meter_location_id
                JOIN market_locations ma ON ma.id = me.market_location_id
                WHERE ma.malo_id = {maLoId}
                  AND s.obis_code = CASE ma.direction
                        WHEN {nameof(EnergyDirection.Generation)} THEN {MeasurementSeries.FedInActiveEnergy}
                        ELSE {MeasurementSeries.ConsumedActiveEnergy} END
                  AND v.interval_start >= {periodStart}
                  AND v.interval_start < {periodEnd}
                GROUP BY 1
                ORDER BY 1
                """)
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new EnergyTotal(
                ToGermanTime(r.BucketStart),
                ToGermanTime(EndOf(r.BucketStart, granularity)),
                r.EnergyKwh,
                r.Intervals,
                r.MeasuredIntervals))
            .ToList();
    }

    private static DateTimeOffset EndOf(DateTimeOffset start, AggregationGranularity granularity)
    {
        var day = GermanCalendar.DayOf(start);
        return granularity switch
        {
            AggregationGranularity.Hour => start.AddHours(1),
            AggregationGranularity.Day => GermanCalendar.StartOfDayUtc(day.AddDays(1)),
            _ => GermanCalendar.StartOfDayUtc(new DateOnly(day.Year, day.Month, 1).AddMonths(1)),
        };
    }

    private static DateTimeOffset ToGermanTime(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, GermanCalendar.TimeZone);

#pragma warning disable CA1812 // Instantiated by EF Core when materializing the raw SQL rows.
    private sealed class TotalRow
#pragma warning restore CA1812
    {
        public DateTimeOffset BucketStart { get; init; }

        public decimal EnergyKwh { get; init; }

        public int Intervals { get; init; }

        public int MeasuredIntervals { get; init; }
    }
}
