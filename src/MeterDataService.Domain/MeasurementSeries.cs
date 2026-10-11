namespace MeterDataService.Domain;

/// <summary>
/// Time series of 15-minute energy values for one measured quantity at a meter location
/// (Lastgang / Messreihe). The quantity is identified by its OBIS code, e.g. <c>1-1:1.29.0</c>
/// for consumed active energy per interval. Values are in kWh.
/// </summary>
public sealed class MeasurementSeries
{
    public const int ObisCodeMaxLength = 32;

    /// <summary>Active energy drawn from the grid per interval (Wirkarbeit Bezug).</summary>
    public const string ConsumedActiveEnergy = "1-1:1.29.0";

    /// <summary>Active energy fed into the grid per interval (Wirkarbeit Lieferung).</summary>
    public const string FedInActiveEnergy = "1-1:2.29.0";

    /// <summary>Market-standard resolution for interval metering (RLM / iMSys).</summary>
    public static readonly TimeSpan IntervalLength = TimeSpan.FromMinutes(15);

    private readonly List<MeasurementValue> _values = [];
    private readonly List<SeriesAnnotation> _annotations = [];

    // Required by EF Core for materialization.
    private MeasurementSeries()
    {
        ObisCode = string.Empty;
    }

    internal MeasurementSeries(Guid meterLocationId, string obisCode)
    {
        if (string.IsNullOrWhiteSpace(obisCode) || obisCode.Length > ObisCodeMaxLength)
        {
            throw new ArgumentException($"OBIS code must be 1–{ObisCodeMaxLength} characters.", nameof(obisCode));
        }

        Id = Guid.CreateVersion7();
        MeterLocationId = meterLocationId;
        ObisCode = obisCode;
    }

    public Guid Id { get; private set; }

    public Guid MeterLocationId { get; private set; }

    /// <summary>OBIS code (Object Identification System) of the measured quantity.</summary>
    public string ObisCode { get; private set; }

    public IReadOnlyCollection<MeasurementValue> Values => _values;

    /// <summary>Anomaly detector findings on intervals of this series.</summary>
    public IReadOnlyCollection<SeriesAnnotation> Annotations => _annotations;

    /// <summary>OBIS code of the series that carries a market location's billed energy in the given direction.</summary>
    public static string EnergyObisCodeFor(EnergyDirection direction) => direction switch
    {
        EnergyDirection.Consumption => ConsumedActiveEnergy,
        EnergyDirection.Generation => FedInActiveEnergy,
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown energy direction."),
    };

    /// <summary>
    /// Stores a value as delivered by the sender. A value already stored for the interval is overwritten,
    /// including a substitute: a later delivery (e.g. a correction, Korrekturlieferung) supersedes what was there.
    /// </summary>
    public MeasurementValue Record(DateTimeOffset intervalStart, decimal value, MeasurementStatus status)
    {
        var utc = intervalStart.ToUniversalTime();
        var existing = _values.Find(v => v.IntervalStart == utc);
        if (existing is null)
        {
            return AddValue(utc, value, status);
        }

        existing.Overwrite(value, status);
        return existing;
    }

    public MeasurementValue AddValue(DateTimeOffset intervalStart, decimal value, MeasurementStatus status)
    {
        var measurement = new MeasurementValue(Id, intervalStart, value, status);
        _values.Add(measurement);
        return measurement;
    }

    /// <summary>
    /// Stores a substitute value (Ersatzwert) for an interval: a rejected value already in the series is
    /// overwritten, a missing one is added. Either way the result gets the trace's
    /// <see cref="ReplacementTrace.ResultingStatus"/>.
    /// </summary>
    public MeasurementValue Substitute(DateTimeOffset intervalStart, decimal value, ReplacementTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);

        var utc = intervalStart.ToUniversalTime();
        var measurement = _values.Find(v => v.IntervalStart == utc)
            ?? AddValue(utc, value, trace.ResultingStatus);
        measurement.Replace(value, trace);
        return measurement;
    }

    public SeriesAnnotation Annotate(DateTimeOffset intervalStart, AnomalyType type, decimal score)
    {
        var annotation = new SeriesAnnotation(Id, intervalStart, type, score);
        _annotations.Add(annotation);
        return annotation;
    }

    /// <summary>
    /// Drops the findings for intervals in [<paramref name="from"/>, <paramref name="to"/>), so a detector run
    /// over a re-imported period replaces the earlier findings instead of piling up duplicates.
    /// </summary>
    public int ClearAnnotations(DateTimeOffset from, DateTimeOffset to)
    {
        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime();
        return _annotations.RemoveAll(a => a.IntervalStart >= fromUtc && a.IntervalStart < toUtc);
    }
}
