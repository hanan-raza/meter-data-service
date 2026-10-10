namespace MeterDataService.Application.MarketLocations;

/// <summary>Read-only access to market location master data; nothing returned is tracked for changes.</summary>
public interface IMarketLocationReadRepository
{
    /// <summary>One page of market locations, ordered by MaLo-ID so pages are stable.</summary>
    Task<IReadOnlyList<MarketLocationDetails>> ListAsync(int offset, int limit, CancellationToken cancellationToken);

    /// <returns>The market location, or <c>null</c> if no market location has this ID.</returns>
    Task<MarketLocationDetails?> FindAsync(string maLoId, CancellationToken cancellationToken);
}
