using MeterDataService.Domain;

namespace MeterDataService.Application.MarketLocations;

/// <summary>Master data (Stammdaten) of a market location (Marktlokation, MaLo) as shown to API clients.</summary>
/// <param name="MaLoId">11-digit market location ID.</param>
/// <param name="Direction">Whether the location consumes or feeds in energy.</param>
/// <param name="EnergyObisCode">OBIS code of the series whose values are billed and aggregated for this location.</param>
/// <param name="MeterLocations">Meter locations (Messlokationen) that measure this market location.</param>
public sealed record MarketLocationDetails(
    string MaLoId, EnergyDirection Direction, string EnergyObisCode, IReadOnlyList<MeterLocationDetails> MeterLocations);

/// <summary>A meter location (Messlokation, MeLo) and what is measured there.</summary>
/// <param name="MeLoId">33-character meter location ID.</param>
/// <param name="InstalledMeter">Serial number (Zählernummer) of the meter installed now, or <c>null</c> if none is.</param>
/// <param name="ObisCodes">OBIS codes of the series recorded at this meter location.</param>
public sealed record MeterLocationDetails(string MeLoId, string? InstalledMeter, IReadOnlyList<string> ObisCodes);
