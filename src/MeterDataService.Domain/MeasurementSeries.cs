namespace MeterDataService.Domain;

/// <summary>
/// Time series of 15-minute energy values for one measured quantity at a meter location
/// (Lastgang / Messreihe). The quantity is identified by its OBIS code, e.g. <c>1-1:1.29.0</c>
/// for consumed active energy per interval. Values are in kWh.
/// </summary>
public sealed class MeasurementSeries
{
    public const int ObisCodeMaxLength = 32;

    /// <summary>Market-standard resolution for interval metering (RLM / iMSys).</summary>
    public static readonly TimeSpan IntervalLength = TimeSpan.FromMinutes(15);

    private readonly List<MeasurementValue> _values = [];

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

    public MeasurementValue AddValue(DateTimeOffset intervalStart, decimal value, MeasurementStatus status)
    {
        var measurement = new MeasurementValue(Id, intervalStart, value, status);
        _values.Add(measurement);
        return measurement;
    }
}
