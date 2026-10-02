namespace MeterDataService.Domain;

/// <summary>
/// Meter location (Messlokation, MeLo): the physical point where energy is measured. Meters
/// are swapped over time (Zählerwechsel) while the meter location and its series stay stable.
/// </summary>
public sealed class MeterLocation
{
    private readonly List<Meter> _meters = [];
    private readonly List<MeasurementSeries> _series = [];

    // Required by EF Core for materialization.
    private MeterLocation()
    {
        MeLoId = string.Empty;
    }

    internal MeterLocation(Guid marketLocationId, string meLoId)
    {
        if (!MeterLocationId.IsValid(meLoId))
        {
            throw new ArgumentException($"'{meLoId}' is not a valid meter location ID.", nameof(meLoId));
        }

        Id = Guid.CreateVersion7();
        MarketLocationId = marketLocationId;
        MeLoId = meLoId;
    }

    public Guid Id { get; private set; }

    /// <summary>33-character meter location ID (Messlokations-ID).</summary>
    public string MeLoId { get; private set; }

    public Guid MarketLocationId { get; private set; }

    public IReadOnlyCollection<Meter> Meters => _meters;

    public IReadOnlyCollection<MeasurementSeries> Series => _series;

    /// <summary>
    /// Installs a meter. Only one meter can be active at a time; the previous one must be removed
    /// first, otherwise two devices would deliver readings for the same interval.
    /// </summary>
    public Meter InstallMeter(string serialNumber, DateTimeOffset installedAt)
    {
        var active = _meters.Find(m => m.RemovedAt is null);
        if (active is not null)
        {
            throw new InvalidOperationException($"Meter '{active.SerialNumber}' is still installed at '{MeLoId}'.");
        }

        if (_meters.Exists(m => m.RemovedAt > installedAt))
        {
            throw new InvalidOperationException("A new meter cannot be installed before the previous one was removed.");
        }

        var meter = new Meter(Id, serialNumber, installedAt);
        _meters.Add(meter);
        return meter;
    }

    public MeasurementSeries AddSeries(string obisCode)
    {
        if (_series.Exists(s => s.ObisCode == obisCode))
        {
            throw new InvalidOperationException($"A series with OBIS code '{obisCode}' already exists at '{MeLoId}'.");
        }

        var series = new MeasurementSeries(Id, obisCode);
        _series.Add(series);
        return series;
    }
}
