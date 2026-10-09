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

        EnsureScale(value, nameof(value));
        EnsureDefined(status);

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

    /// <summary>
    /// Algorithm that substituted this value (Ersatzwertverfahren). Null for metered values and for
    /// substitutes the sender delivered already replaced, whose derivation this service can't know.
    /// </summary>
    public ReplacementMethod? ReplacedBy { get; private set; }

    /// <summary>Last usable value before the gap that the substitute was derived from, in kWh.</summary>
    public decimal? AnchorValueBefore { get; private set; }

    /// <summary>First usable value after the gap that the substitute was derived from, in kWh.</summary>
    public decimal? AnchorValueAfter { get; private set; }

    /// <summary>German calendar day the substitute was copied from (Vergleichstag), for similar-day substitutes.</summary>
    public DateOnly? SourceDay { get; private set; }

    /// <summary>
    /// Overwrites the value with a substitute (Ersatzwert). The previous value is not kept here; the
    /// validation report of the import that rejected it records what was delivered and why it was unusable.
    /// </summary>
    internal void Replace(decimal value, ReplacementTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        EnsureScale(value, nameof(value));

        Value = value;
        Status = trace.ResultingStatus;
        ReplacedBy = trace.Method;
        AnchorValueBefore = trace.AnchorValueBefore;
        AnchorValueAfter = trace.AnchorValueAfter;
        SourceDay = trace.SourceDay;
    }

    /// <summary>
    /// Takes a newly delivered value. The trace of an earlier substitute is dropped: the value now comes from
    /// the sender, and the sender's own derivation (if it delivered a substitute) is unknown here.
    /// </summary>
    internal void Overwrite(decimal value, MeasurementStatus status)
    {
        EnsureScale(value, nameof(value));
        EnsureDefined(status);

        Value = value;
        Status = status;
        ReplacedBy = null;
        AnchorValueBefore = null;
        AnchorValueAfter = null;
        SourceDay = null;
    }

    private static void EnsureDefined(MeasurementStatus status)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown measurement status.");
        }
    }

    private static void EnsureScale(decimal value, string paramName)
    {
        if (decimal.Round(value, Scale) != value)
        {
            throw new ArgumentException($"Energy value {value} has more than {Scale} decimal places.", paramName);
        }
    }

    // German offsets are whole hours, so UTC alignment implies local alignment.
    private static bool IsAlignedToInterval(DateTimeOffset utc) =>
        utc.Ticks % MeasurementSeries.IntervalLength.Ticks == 0;
}
