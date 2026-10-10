using MeterDataService.Application.Aggregation;
using MeterDataService.Application.MarketLocations;

namespace MeterDataService.Application.Measurements;

/// <summary>
/// Reads a market location's energy series: the stored 15-minute values with their status and substitution trace,
/// or sums per hour, day or month computed by the database.
/// </summary>
public sealed class MeasurementQueryService(
    IMarketLocationReadRepository marketLocations,
    IMeasurementReadRepository measurements,
    IAggregationRepository aggregation)
{
    /// <returns>The series, or <c>null</c> if the market location is unknown.</returns>
    public async Task<MeasurementSeriesView?> GetSeriesAsync(
        string maLoId, DateOnly from, DateOnly to, AggregationGranularity granularity, CancellationToken cancellationToken)
    {
        if (ReportingPeriod.Validate(from, to, granularity) is { } error)
        {
            throw new ArgumentException(error, nameof(to));
        }

        // Looked up first so an unknown MaLo is a 404, not an empty series that looks like missing data.
        var location = await marketLocations.FindAsync(maLoId, cancellationToken);
        if (location is null)
        {
            return null;
        }

        IReadOnlyList<MeasurementPoint> points = granularity == AggregationGranularity.QuarterHour
            ? (await measurements.GetEnergyValuesAsync(maLoId, from, to, cancellationToken)).Select(MeasurementPoint.From).ToList()
            : (await aggregation.GetTotalsAsync(maLoId, granularity, from, to, cancellationToken)).Select(MeasurementPoint.From).ToList();

        return new MeasurementSeriesView(location.MaLoId, location.EnergyObisCode, granularity, from, to, points);
    }
}
