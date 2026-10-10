using MeterDataService.Domain;

namespace MeterDataService.Application.Measurements;

/// <summary>Read-only access to stored 15-minute values; nothing returned is tracked for changes.</summary>
public interface IMeasurementReadRepository
{
    /// <summary>
    /// Values of the series that carries a market location's billed energy (OBIS code by direction) for a
    /// period of German calendar days, ordered by interval start. Empty if the MaLo is unknown.
    /// </summary>
    Task<IReadOnlyList<MeasurementValue>> GetEnergyValuesAsync(
        string maLoId, DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken);
}
