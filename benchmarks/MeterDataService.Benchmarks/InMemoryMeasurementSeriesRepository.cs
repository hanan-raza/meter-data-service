using MeterDataService.Application.Import;
using MeterDataService.Domain;

namespace MeterDataService.Benchmarks;

/// <summary>
/// Stands in for the PostgreSQL repository, so the benchmark measures the pipeline and not the database.
/// Every lookup hands out a fresh series with the scenario's history, as a new unit of work would.
/// </summary>
internal sealed class InMemoryMeasurementSeriesRepository(ImportScenario scenario) : IMeasurementSeriesRepository
{
    public MeasurementSeries? Series { get; private set; }

    public Task<MeasurementSeries?> FindEnergySeriesAsync(
        string maLoId, DateTimeOffset valuesFrom, DateTimeOffset valuesTo, CancellationToken cancellationToken)
    {
        Series = maLoId == ImportScenario.MaLoId ? scenario.NewSeriesWithHistory() : null;
        return Task.FromResult(Series);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
