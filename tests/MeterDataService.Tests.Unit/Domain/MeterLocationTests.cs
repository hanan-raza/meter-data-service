using MeterDataService.Domain;
using Shouldly;

namespace MeterDataService.Tests.Unit.Domain;

public class MeterLocationTests
{
    private const string MaLoId = "41373559241";
    private const string MeLoId = "DE0001234567890000000000000000001";

    private static readonly DateTimeOffset Jan1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(1));

    [Fact]
    public void MarketLocation_rejects_invalid_id() =>
        Should.Throw<ArgumentException>(() => new MarketLocation("12345678901", EnergyDirection.Consumption));

    [Fact]
    public void AddMeterLocation_links_meter_location_to_market_location()
    {
        var malo = new MarketLocation(MaLoId, EnergyDirection.Consumption);

        var melo = malo.AddMeterLocation(MeLoId);

        melo.MarketLocationId.ShouldBe(malo.Id);
        malo.MeterLocations.ShouldHaveSingleItem().ShouldBeSameAs(melo);
    }

    [Fact]
    public void AddMeterLocation_rejects_duplicate()
    {
        var malo = new MarketLocation(MaLoId, EnergyDirection.Consumption);
        malo.AddMeterLocation(MeLoId);

        Should.Throw<InvalidOperationException>(() => malo.AddMeterLocation(MeLoId));
    }

    [Fact]
    public void InstallMeter_rejects_second_meter_while_first_is_installed()
    {
        var melo = NewMeterLocation();
        melo.InstallMeter("1ESY1160000001", Jan1);

        Should.Throw<InvalidOperationException>(() => melo.InstallMeter("1ESY1160000002", Jan1.AddMonths(1)));
    }

    [Fact]
    public void InstallMeter_allows_meter_exchange_after_removal()
    {
        var melo = NewMeterLocation();
        var oldMeter = melo.InstallMeter("1ESY1160000001", Jan1);
        oldMeter.Remove(Jan1.AddMonths(6));

        var newMeter = melo.InstallMeter("1ESY1160000002", Jan1.AddMonths(6));

        melo.Meters.Count.ShouldBe(2);
        newMeter.RemovedAt.ShouldBeNull();
    }

    [Fact]
    public void InstallMeter_rejects_installation_before_previous_removal()
    {
        var melo = NewMeterLocation();
        melo.InstallMeter("1ESY1160000001", Jan1).Remove(Jan1.AddMonths(6));

        Should.Throw<InvalidOperationException>(() => melo.InstallMeter("1ESY1160000002", Jan1.AddMonths(5)));
    }

    [Fact]
    public void Meter_stores_installation_time_in_utc()
    {
        var meter = NewMeterLocation().InstallMeter("1ESY1160000001", Jan1);

        meter.InstalledAt.Offset.ShouldBe(TimeSpan.Zero);
        meter.InstalledAt.ShouldBe(Jan1);
    }

    [Fact]
    public void Meter_removal_must_be_after_installation()
    {
        var meter = NewMeterLocation().InstallMeter("1ESY1160000001", Jan1);

        Should.Throw<ArgumentOutOfRangeException>(() => meter.Remove(Jan1));
    }

    [Fact]
    public void AddSeries_rejects_duplicate_obis_code()
    {
        var melo = NewMeterLocation();
        melo.AddSeries("1-1:1.29.0");

        Should.Throw<InvalidOperationException>(() => melo.AddSeries("1-1:1.29.0"));
    }

    private static MeterLocation NewMeterLocation() =>
        new MarketLocation(MaLoId, EnergyDirection.Consumption).AddMeterLocation(MeLoId);
}
