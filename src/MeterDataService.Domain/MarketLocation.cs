namespace MeterDataService.Domain;

/// <summary>
/// Market location (Marktlokation, MaLo): the point at which energy is supplied or fed in and
/// billed. Consumption is aggregated per market location, not per meter.
/// </summary>
public sealed class MarketLocation
{
    private readonly List<MeterLocation> _meterLocations = [];

    // Required by EF Core for materialization.
    private MarketLocation()
    {
        MaLoId = string.Empty;
    }

    public MarketLocation(string maLoId, EnergyDirection direction)
    {
        if (!MarketLocationId.IsValid(maLoId))
        {
            throw new ArgumentException($"'{maLoId}' is not a valid market location ID.", nameof(maLoId));
        }

        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown energy direction.");
        }

        Id = Guid.CreateVersion7();
        MaLoId = maLoId;
        Direction = direction;
    }

    public Guid Id { get; private set; }

    /// <summary>11-digit market location ID (Marktlokations-ID).</summary>
    public string MaLoId { get; private set; }

    public EnergyDirection Direction { get; private set; }

    public IReadOnlyCollection<MeterLocation> MeterLocations => _meterLocations;

    public MeterLocation AddMeterLocation(string meLoId)
    {
        if (_meterLocations.Exists(m => m.MeLoId == meLoId))
        {
            throw new InvalidOperationException($"Meter location '{meLoId}' is already assigned to market location '{MaLoId}'.");
        }

        var meterLocation = new MeterLocation(Id, meLoId);
        _meterLocations.Add(meterLocation);
        return meterLocation;
    }
}
