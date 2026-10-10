namespace MeterDataService.Application.Aggregation;

public static class AggregationRepositoryExtensions
{
    /// <summary>Totals in the requested bucket length; <see cref="AggregationGranularity.QuarterHour"/> is not a sum.</summary>
    public static Task<IReadOnlyList<EnergyTotal>> GetTotalsAsync(
        this IAggregationRepository repository,
        string maLoId,
        AggregationGranularity granularity,
        DateOnly firstDay,
        DateOnly lastDay,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);

        return granularity switch
        {
            AggregationGranularity.Hour => repository.GetHourlyTotalsAsync(maLoId, firstDay, lastDay, cancellationToken),
            AggregationGranularity.Day => repository.GetDailyTotalsAsync(maLoId, firstDay, lastDay, cancellationToken),
            AggregationGranularity.Month => repository.GetMonthlyTotalsAsync(maLoId, firstDay, lastDay, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(granularity), granularity, "Only hours, days and months are summed."),
        };
    }
}
