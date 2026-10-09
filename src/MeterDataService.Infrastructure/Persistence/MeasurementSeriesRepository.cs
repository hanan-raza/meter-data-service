using MeterDataService.Application.Import;
using MeterDataService.Domain;
using Microsoft.EntityFrameworkCore;

namespace MeterDataService.Infrastructure.Persistence;

internal sealed class MeasurementSeriesRepository(MeterDataDbContext context) : IMeasurementSeriesRepository
{
    public async Task<MeasurementSeries?> FindEnergySeriesAsync(
        string maLoId, DateTimeOffset valuesFrom, DateTimeOffset valuesTo, CancellationToken cancellationToken)
    {
        var marketLocation = await context.MarketLocations
            .AsNoTracking()
            .Where(m => m.MaLoId == maLoId)
            .Select(m => new { m.Id, m.Direction })
            .SingleOrDefaultAsync(cancellationToken);
        if (marketLocation is null)
        {
            return null;
        }

        var obisCode = MeasurementSeries.EnergyObisCodeFor(marketLocation.Direction);
        var fromUtc = valuesFrom.ToUniversalTime();
        var toUtc = valuesTo.ToUniversalTime();

        // Filtered include: a series holds years of values, the import only needs a few weeks of them.
        // Several meter locations with an energy series (e.g. sum metering) would be ambiguous here, so Single.
        return await context.MeasurementSeries
            .Include(s => s.Values.Where(v => v.IntervalStart >= fromUtc && v.IntervalStart < toUtc))
            .Where(s => s.ObisCode == obisCode
                && context.MeterLocations.Any(m => m.Id == s.MeterLocationId && m.MarketLocationId == marketLocation.Id))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}
