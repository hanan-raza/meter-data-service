namespace MeterDataService.Application.Aggregation;

/// <summary>
/// Totals of the billed energy of a market location (Marktlokation, MaLo) over a period of German calendar days:
/// the values of its energy series (OBIS code by direction), summed per bucket.
/// </summary>
/// <remarks>
/// Buckets without any value are left out, so a caller can tell "no data" from "zero consumption". Buckets at the
/// edge of the period only hold the period's values, e.g. a monthly total for the 10th to the 20th.
/// </remarks>
public interface IAggregationRepository
{
    Task<IReadOnlyList<EnergyTotal>> GetHourlyTotalsAsync(string maLoId, DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken);

    Task<IReadOnlyList<EnergyTotal>> GetDailyTotalsAsync(string maLoId, DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken);

    Task<IReadOnlyList<EnergyTotal>> GetMonthlyTotalsAsync(string maLoId, DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken);
}
