using MeterDataService.Application.Import;
using MeterDataService.Domain;
using MeterDataService.Infrastructure;
using MeterDataService.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MeterDataService.Tests.Integration.Persistence;

[Trait("Category", "Integration")]
public sealed class MeasurementSeriesRepositoryTests(PostgreSqlFixture database) : IntegrationTestBase(database), IDisposable
{
    private static readonly TimeSpan SummerTime = TimeSpan.FromHours(2);

    private ServiceProvider? _services;

    [SkippableFact]
    public async Task Loads_only_the_values_in_the_requested_range()
    {
        var seriesId = await SaveMarketLocationAsync("41373559241", EnergyDirection.Consumption, s =>
        {
            s.AddValue(At(9, 45), 0.1m, MeasurementStatus.Measured);
            s.AddValue(At(10, 0), 0.2m, MeasurementStatus.Measured);
            s.AddValue(At(10, 45), 0.3m, MeasurementStatus.Measured);
            s.AddValue(At(11, 0), 0.4m, MeasurementStatus.Measured);
        });

        await using var scope = CreateScope();
        var series = await Repository(scope).FindEnergySeriesAsync("41373559241", At(10, 0), At(11, 0), CancellationToken.None);

        series.ShouldNotBeNull().Id.ShouldBe(seriesId);
        series.Values.Select(v => v.Value).Order().ShouldBe([0.2m, 0.3m]);
    }

    [SkippableFact]
    public async Task Changes_to_the_loaded_series_are_saved_in_one_unit_of_work()
    {
        var seriesId = await SaveMarketLocationAsync("10000000009", EnergyDirection.Consumption, s =>
            s.AddValue(At(10, 0), 0.2m, MeasurementStatus.Measured));

        await using (var scope = CreateScope())
        {
            var repository = Repository(scope);
            var series = (await repository.FindEnergySeriesAsync("10000000009", At(0, 0), At(23, 0), CancellationToken.None)).ShouldNotBeNull();
            series.Record(At(10, 0), 0.25m, MeasurementStatus.Measured);
            series.Record(At(10, 15), 0.5m, MeasurementStatus.Measured);
            series.Substitute(At(10, 30), 0.75m, ReplacementTrace.LinearInterpolation(0.5m, 1m));
            await repository.SaveChangesAsync(CancellationToken.None);
        }

        await using var context = CreateDbContext();
        var stored = await context.MeasurementValues
            .Where(v => v.MeasurementSeriesId == seriesId)
            .OrderBy(v => v.IntervalStart)
            .ToListAsync();
        stored.Select(v => v.Value).ShouldBe([0.25m, 0.5m, 0.75m]);
        stored[2].ReplacedBy.ShouldBe(ReplacementMethod.LinearInterpolation);
    }

    [SkippableFact]
    public async Task Picks_the_series_matching_the_market_location_direction()
    {
        // A PV meter location also records the small consumption of the inverter at night.
        var marketLocation = new MarketLocation("20000000008", EnergyDirection.Generation);
        var meterLocation = marketLocation.AddMeterLocation("DE0001234567890000000000000000008");
        meterLocation.AddSeries(MeasurementSeries.ConsumedActiveEnergy);
        var fedIn = meterLocation.AddSeries(MeasurementSeries.FedInActiveEnergy);
        await SaveAsync(marketLocation);

        await using var scope = CreateScope();
        var series = await Repository(scope).FindEnergySeriesAsync("20000000008", At(0, 0), At(23, 0), CancellationToken.None);

        series.ShouldNotBeNull().Id.ShouldBe(fedIn.Id);
    }

    [SkippableFact]
    public async Task Unknown_market_location_gives_null()
    {
        await using var scope = CreateScope();

        var series = await Repository(scope).FindEnergySeriesAsync("30000000006", At(0, 0), At(23, 0), CancellationToken.None);

        series.ShouldBeNull();
    }

    private static DateTimeOffset At(int hour, int minute) => new(2026, 6, 10, hour, minute, 0, SummerTime);

    private static IMeasurementSeriesRepository Repository(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IMeasurementSeriesRepository>();

    public void Dispose() => _services?.Dispose();

    // Resolved through the real registration, so the test also covers the DI wiring. Built lazily: the base
    // constructor skips the test without Docker, and there is no connection string before that.
    private AsyncServiceScope CreateScope() =>
        (_services ??= new ServiceCollection().AddInfrastructure(Database.ConnectionString).BuildServiceProvider())
            .CreateAsyncScope();

    // Each test uses its own MaLo/MeLo because both IDs are unique and the database is shared per class.
    private async Task<Guid> SaveMarketLocationAsync(string maLoId, EnergyDirection direction, Action<MeasurementSeries> addValues)
    {
        var marketLocation = new MarketLocation(maLoId, direction);
        var series = marketLocation
            .AddMeterLocation($"DE00012345678900000000000{maLoId[..8]}")
            .AddSeries(MeasurementSeries.EnergyObisCodeFor(direction));
        addValues(series);
        await SaveAsync(marketLocation);
        return series.Id;
    }

    private async Task SaveAsync(MarketLocation marketLocation)
    {
        await using var context = CreateDbContext();
        context.MarketLocations.Add(marketLocation);
        await context.SaveChangesAsync();
    }
}
