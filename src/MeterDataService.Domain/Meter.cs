namespace MeterDataService.Domain;

/// <summary>Physical metering device (Zähler) installed at a meter location for a period of time.</summary>
public sealed class Meter
{
    public const int SerialNumberMaxLength = 64;

    // Required by EF Core for materialization.
    private Meter()
    {
        SerialNumber = string.Empty;
    }

    internal Meter(Guid meterLocationId, string serialNumber, DateTimeOffset installedAt)
    {
        if (string.IsNullOrWhiteSpace(serialNumber) || serialNumber.Length > SerialNumberMaxLength)
        {
            throw new ArgumentException($"Serial number must be 1–{SerialNumberMaxLength} characters.", nameof(serialNumber));
        }

        Id = Guid.CreateVersion7();
        MeterLocationId = meterLocationId;
        SerialNumber = serialNumber;
        InstalledAt = installedAt.ToUniversalTime();
    }

    public Guid Id { get; private set; }

    /// <summary>Manufacturer serial number (Zählernummer).</summary>
    public string SerialNumber { get; private set; }

    public Guid MeterLocationId { get; private set; }

    /// <summary>Installation instant (Einbaudatum), stored in UTC.</summary>
    public DateTimeOffset InstalledAt { get; private set; }

    /// <summary>Removal instant (Ausbaudatum), stored in UTC; <c>null</c> while installed.</summary>
    public DateTimeOffset? RemovedAt { get; private set; }

    public void Remove(DateTimeOffset removedAt)
    {
        if (RemovedAt is not null)
        {
            throw new InvalidOperationException($"Meter '{SerialNumber}' has already been removed.");
        }

        var utc = removedAt.ToUniversalTime();
        if (utc <= InstalledAt)
        {
            throw new ArgumentOutOfRangeException(nameof(removedAt), removedAt, "Removal must be after installation.");
        }

        RemovedAt = utc;
    }
}
