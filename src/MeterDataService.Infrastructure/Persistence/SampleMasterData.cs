using MeterDataService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MeterDataService.Infrastructure.Persistence;

/// <summary>
/// Master data (Stammdaten) for the market location used in the README and the <c>.http</c> file. Values for an
/// unknown market location are refused, so without it the quick start's first import would fail.
/// </summary>
public static class SampleMasterData
{
    /// <summary>Household market location (consumption) the sample CSV files are imported into.</summary>
    public const string MaLoId = "41373559241";

    public const string MeLoId = "DE0001234567890000000000000041373";

    /// <summary>Creates the sample market location with one meter location and its energy series, unless it exists.</summary>
    public static async Task EnsureAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MeterDataDbContext>();

        if (await context.MarketLocations.AnyAsync(m => m.MaLoId == MaLoId, cancellationToken))
        {
            return;
        }

        var marketLocation = new MarketLocation(MaLoId, EnergyDirection.Consumption);
        marketLocation.AddMeterLocation(MeLoId).AddSeries(MeasurementSeries.EnergyObisCodeFor(marketLocation.Direction));
        context.MarketLocations.Add(marketLocation);
        await context.SaveChangesAsync(cancellationToken);
    }
}
