using MeterDataService.Domain;

namespace MeterDataService.Application.Import;

/// <summary>
/// Loads and stores measurement series for the import. One instance is one unit of work: everything loaded
/// is tracked, and <see cref="SaveChangesAsync"/> writes all changes of the job in a single transaction.
/// </summary>
public interface IMeasurementSeriesRepository
{
    /// <summary>
    /// Finds the series that carries the billed energy of a market location (Marktlokation, MaLo): the one whose
    /// OBIS code matches the location's direction (<see cref="MeasurementSeries.EnergyObisCodeFor"/>).
    /// </summary>
    /// <param name="maLoId">11-digit market location ID.</param>
    /// <param name="valuesFrom">Inclusive start of the values to load.</param>
    /// <param name="valuesTo">Exclusive end of the values to load.</param>
    /// <param name="cancellationToken">Aborts the query.</param>
    /// <returns>The series with only the values in the range loaded, or <c>null</c> if the MaLo or its series is unknown.</returns>
    Task<MeasurementSeries?> FindEnergySeriesAsync(
        string maLoId, DateTimeOffset valuesFrom, DateTimeOffset valuesTo, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
