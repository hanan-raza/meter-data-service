namespace MeterDataService.Domain;

/// <summary>
/// Energy quantity of a single 15-minute interval (Messwert / Intervallwert), identified by the
/// UTC start of the interval. Storing UTC keeps DST days unambiguous: the repeated 02:00–03:00
/// hour in October maps to two distinct UTC instants.
/// </summary>
public sealed class MeasurementValue
{
    /// <summary>Decimal places persisted for energy values; more would be silently rounded by the database.</summary>
    public const int Scale = 5;

    public const int Precision = 18;

    // Required by EF Core for materialization.
    private MeasurementValue()
    {
    }

    public MeasurementValue(Guid measurementSeriesId, DateTimeOffset intervalStart, decimal value, MeasurementStatus status)
    {
        var utc = intervalStart.ToUniversalTime();
        if (!IsAlignedToInterval(utc))
        {
            throw new ArgumentException(
                $"Interval start {intervalStart:O} is not aligned to a {MeasurementSeries.IntervalLength.TotalMinutes}-minute boundary.",
                nameof(intervalStart));
        }

        if (decimal.Round(value, Scale) != value)
        {
            throw new ArgumentException($"Energy value {value} has more than {Scale} decimal places.", nameof(value));
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown measurement status.");
        }

        MeasurementSeriesId = measurementSeriesId;
        IntervalStart = utc;
        Value = value;
        Status = status;
    }

    public long Id { get; private set; }

    public Guid MeasurementSeriesId { get; private set; }

    /// <summary>Inclusive start of the interval, in UTC.</summary>
    public DateTimeOffset IntervalStart { get; private set; }

    /// <summary>Exclusive end of the interval, in UTC.</summary>
    public DateTimeOffset IntervalEnd => IntervalStart + MeasurementSeries.IntervalLength;

    /// <summary>Energy in kWh. Negative raw values are accepted here and rejected by validation, so they remain traceable.</summary>
    public decimal Value { get; private set; }

    public MeasurementStatus Status { get; private set; }

    // German offsets are whole hours, so UTC alignment implies local alignment.
    private static bool IsAlignedToInterval(DateTimeOffset utc) =>
        utc.Ticks % MeasurementSeries.IntervalLength.Ticks == 0;
}
