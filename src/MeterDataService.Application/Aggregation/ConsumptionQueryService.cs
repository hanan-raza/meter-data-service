using MeterDataService.Application.MarketLocations;

namespace MeterDataService.Application.Aggregation;

/// <summary>Builds the <see cref="ConsumptionReport"/> of a market location from the database totals.</summary>
public sealed class ConsumptionQueryService(IMarketLocationReadRepository marketLocations, IAggregationRepository aggregation)
{
    /// <returns>The report, or <c>null</c> if the market location is unknown.</returns>
    public async Task<ConsumptionReport?> GetReportAsync(
        string maLoId, DateOnly from, DateOnly to, AggregationGranularity granularity, CancellationToken cancellationToken)
    {
        if (granularity == AggregationGranularity.QuarterHour)
        {
            throw new ArgumentOutOfRangeException(nameof(granularity), granularity, "A consumption report sums per hour, day or month.");
        }

        if (ReportingPeriod.Validate(from, to, granularity) is { } error)
        {
            throw new ArgumentException(error, nameof(to));
        }

        var location = await marketLocations.FindAsync(maLoId, cancellationToken);
        if (location is null)
        {
            return null;
        }

        var totals = await aggregation.GetTotalsAsync(maLoId, granularity, from, to, cancellationToken);
        return ConsumptionReport.Create(location.MaLoId, location.Direction, from, to, granularity, totals);
    }
}
