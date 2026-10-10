using MeterDataService.Application.MarketLocations;
using MeterDataService.Domain;
using Microsoft.EntityFrameworkCore;

namespace MeterDataService.Infrastructure.Persistence;

internal sealed class MarketLocationReadRepository(MeterDataDbContext context) : IMarketLocationReadRepository
{
    public async Task<IReadOnlyList<MarketLocationDetails>> ListAsync(int offset, int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var rows = await Project(context.MarketLocations.OrderBy(m => m.MaLoId).Skip(offset).Take(limit))
            .ToListAsync(cancellationToken);
        return rows.Select(ToDetails).ToList();
    }

    public async Task<MarketLocationDetails?> FindAsync(string maLoId, CancellationToken cancellationToken)
    {
        var row = await Project(context.MarketLocations.Where(m => m.MaLoId == maLoId))
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : ToDetails(row);
    }

    // A projection instead of Include: the meters' history and the series' values are never loaded.
    private static IQueryable<MarketLocationRow> Project(IQueryable<MarketLocation> query) =>
        query
            .AsNoTracking()
            .Select(m => new MarketLocationRow(
                m.MaLoId,
                m.Direction,
                m.MeterLocations
                    .OrderBy(me => me.MeLoId)
                    .Select(me => new MeterLocationDetails(
                        me.MeLoId,
                        me.Meters.Where(x => x.RemovedAt == null).Select(x => x.SerialNumber).FirstOrDefault(),
                        me.Series.OrderBy(s => s.ObisCode).Select(s => s.ObisCode).ToList()))
                    .ToList()));

    private static MarketLocationDetails ToDetails(MarketLocationRow row) =>
        new(row.MaLoId, row.Direction, MeasurementSeries.EnergyObisCodeFor(row.Direction), row.MeterLocations);

    private sealed record MarketLocationRow(string MaLoId, EnergyDirection Direction, List<MeterLocationDetails> MeterLocations);
}
