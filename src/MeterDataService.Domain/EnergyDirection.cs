namespace MeterDataService.Domain;

/// <summary>Energy flow direction of a market location (Energierichtung).</summary>
public enum EnergyDirection
{
    /// <summary>Withdrawal from the grid (Ausspeisung / Verbrauch).</summary>
    Consumption = 1,

    /// <summary>Injection into the grid, e.g. PV feed-in (Einspeisung / Erzeugung).</summary>
    Generation = 2,
}
