using System.Net;
using System.Net.Http.Json;
using MeterDataService.Application.MarketLocations;
using MeterDataService.Domain;
using MeterDataService.Infrastructure.Persistence;
using MeterDataService.Tests.Integration.Infrastructure;
using Shouldly;

namespace MeterDataService.Tests.Integration.Api;

[Trait("Category", "Integration")]
public sealed class MarketLocationEndpointTests(PostgreSqlFixture database) : ApiTestBase(database)
{
    [SkippableFact]
    public async Task Get_returns_master_data_with_the_installed_meter_and_all_series()
    {
        const string maLo = "20000000008";
        const string meLo = "DE0001234567890000000000020000000";
        var marketLocation = new MarketLocation(maLo, EnergyDirection.Generation);
        var meterLocation = marketLocation.AddMeterLocation(meLo);
        meterLocation.InstallMeter("OLD-1", new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero))
            .Remove(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        meterLocation.InstallMeter("NEW-2", new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        meterLocation.AddSeries(MeasurementSeries.FedInActiveEnergy);
        meterLocation.AddSeries(MeasurementSeries.ConsumedActiveEnergy);
        await SaveAsync(marketLocation);
        using var client = Factory.CreateClient();

        var details = await client.GetFromJsonAsync<MarketLocationDetails>(new Uri($"/api/market-locations/{maLo}", UriKind.Relative), Json);

        details.ShouldNotBeNull();
        details.MaLoId.ShouldBe(maLo);
        details.Direction.ShouldBe(EnergyDirection.Generation);
        details.EnergyObisCode.ShouldBe(MeasurementSeries.FedInActiveEnergy);
        var location = details.MeterLocations.ShouldHaveSingleItem();
        location.MeLoId.ShouldBe(meLo);
        location.InstalledMeter.ShouldBe("NEW-2");
        location.ObisCodes.ShouldBe([MeasurementSeries.ConsumedActiveEnergy, MeasurementSeries.FedInActiveEnergy]);
    }

    [SkippableFact]
    public async Task List_is_ordered_by_malo_id_and_pages_do_not_overlap()
    {
        await SaveAsync(new MarketLocation("30000000007", EnergyDirection.Consumption));
        await SaveAsync(new MarketLocation("40000000006", EnergyDirection.Consumption));
        using var client = Factory.CreateClient();

        var all = await ListAsync(client, "?limit=500");
        var firstPage = await ListAsync(client, "?offset=0&limit=2");
        var secondPage = await ListAsync(client, "?offset=2&limit=2");

        all.Select(m => m.MaLoId).ShouldBe(all.Select(m => m.MaLoId).Order(StringComparer.Ordinal));
        all.Select(m => m.MaLoId).ShouldContain(SampleMasterData.MaLoId);
        all.Select(m => m.MaLoId).ShouldContain("30000000007");
        firstPage.Concat(secondPage).Select(m => m.MaLoId).ShouldBe(all.Take(4).Select(m => m.MaLoId));
    }

    [SkippableTheory]
    [InlineData("?limit=0")]
    [InlineData("?limit=501")]
    [InlineData("?offset=-1")]
    public async Task List_with_out_of_range_paging_is_rejected_with_400(string query)
    {
        using var client = Factory.CreateClient();

        using var response = await client.GetAsync(new Uri($"/api/market-locations{query}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task Invalid_malo_id_is_rejected_with_400()
    {
        using var client = Factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/market-locations/41373559242", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("maLoId");
    }

    [SkippableFact]
    public async Task Unknown_market_location_returns_404_problem()
    {
        using var client = Factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/market-locations/90000000001", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("application/problem+json");
    }

    private static async Task<IReadOnlyList<MarketLocationDetails>> ListAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<IReadOnlyList<MarketLocationDetails>>(new Uri($"/api/market-locations{query}", UriKind.Relative), Json))
            .ShouldNotBeNull();

    private async Task SaveAsync(MarketLocation marketLocation)
    {
        await using var context = CreateDbContext();
        context.MarketLocations.Add(marketLocation);
        await context.SaveChangesAsync();
    }
}
