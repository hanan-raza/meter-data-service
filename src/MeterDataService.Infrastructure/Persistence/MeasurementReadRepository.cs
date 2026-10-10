using MeterDataService.Application.Measurements;
using MeterDataService.Domain;
using Microsoft.EntityFrameworkCore;

namespace MeterDataService.Infrastructure.Persistence;

internal sealed class MeasurementReadRepository(MeterDataDbContext context) : IMeasurementReadRepository
{
    public async Task<IReadOnlyList<MeasurementValue>> GetEnergyValuesAsync(
        string maLoId, DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken)
    {
        if (lastDay < firstDay)
        {
            throw new ArgumentException($"Last day {lastDay:O} is before first day {firstDay:O}.", nameof(lastDay));
        }

        var direction = await context.MarketLocations
            .AsNoTracking()
            .Where(m => m.MaLoId == maLoId)
            .Select(m => (EnergyDirection?)m.Direction)
            .SingleOrDefaultAsync(cancellationToken);
        if (direction is not { } known)
        {
            return [];
        }

        var obisCode = MeasurementSeries.EnergyObisCodeFor(known);
        var periodStart = GermanCalendar.StartOfDayUtc(firstDay);
        var periodEnd = GermanCalendar.StartOfDayUtc(lastDay.AddDays(1));

        return await context.MeasurementValues
            .AsNoTracking()
            .Where(v => v.IntervalStart >= periodStart && v.IntervalStart < periodEnd
                && context.MeasurementSeries.Any(s => s.Id == v.MeasurementSeriesId
                    && s.ObisCode == obisCode
                    && context.MeterLocations.Any(me => me.Id == s.MeterLocationId
                        && context.MarketLocations.Any(ma => ma.Id == me.MarketLocationId && ma.MaLoId == maLoId))))
            .OrderBy(v => v.IntervalStart)
            .ToListAsync(cancellationToken);
    }
}
