using MeterDataService.Application.Import;
using MeterDataService.Domain;

namespace MeterDataService.Tests.Unit.Application;

/// <summary>Holds one series in memory, as if every value of it had been loaded.</summary>
internal sealed class FakeMeasurementSeriesRepository : IMeasurementSeriesRepository
{
    public MeasurementSeries Series { get; } = new MarketLocation("41373559241", EnergyDirection.Consumption)
        .AddMeterLocation("DE0001234567890000000000000000001")
        .AddSeries(MeasurementSeries.ConsumedActiveEnergy);

    public bool Known { get; set; } = true;

    public (string MaLoId, DateTimeOffset From, DateTimeOffset To)? LastQuery { get; private set; }

    public int Saves { get; private set; }

    public Task<MeasurementSeries?> FindEnergySeriesAsync(
        string maLoId, DateTimeOffset valuesFrom, DateTimeOffset valuesTo, CancellationToken cancellationToken)
    {
        LastQuery = (maLoId, valuesFrom, valuesTo);
        return Task.FromResult(Known ? Series : null);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return Task.CompletedTask;
    }
}
