using MeterDataService.Domain;
using Shouldly;

namespace MeterDataService.Tests.Unit.Domain;

public class MeasurementValueTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    [Fact]
    public void Interval_start_is_normalized_to_utc()
    {
        var series = NewSeries();
        var localStart = new DateTimeOffset(2026, 7, 1, 12, 15, 0, TimeSpan.FromHours(2));

        var value = series.AddValue(localStart, 1.25m, MeasurementStatus.Measured);

        value.IntervalStart.Offset.ShouldBe(TimeSpan.Zero);
        value.IntervalStart.ShouldBe(localStart);
        value.IntervalEnd.ShouldBe(localStart.AddMinutes(15));
        value.MeasurementSeriesId.ShouldBe(series.Id);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(14)]
    public void Interval_start_must_be_on_quarter_hour(int minute)
    {
        var start = new DateTimeOffset(2026, 7, 1, 12, minute, 0, TimeSpan.Zero);

        Should.Throw<ArgumentException>(() => NewSeries().AddValue(start, 1m, MeasurementStatus.Measured));
    }

    [Fact]
    public void Interval_start_with_seconds_is_rejected()
    {
        var start = new DateTimeOffset(2026, 7, 1, 12, 15, 1, TimeSpan.Zero);

        Should.Throw<ArgumentException>(() => NewSeries().AddValue(start, 1m, MeasurementStatus.Measured));
    }

    [Fact]
    public void Value_with_more_than_five_decimals_is_rejected_instead_of_silently_rounded() =>
        Should.Throw<ArgumentException>(() =>
            NewSeries().AddValue(DateTimeOffset.UnixEpoch, 0.123456m, MeasurementStatus.Measured));

    [Fact]
    public void Negative_raw_value_is_kept_for_later_validation()
    {
        var value = NewSeries().AddValue(DateTimeOffset.UnixEpoch, -0.5m, MeasurementStatus.Measured);

        value.Value.ShouldBe(-0.5m);
    }

    [Fact]
    public void Undefined_status_is_rejected() =>
        Should.Throw<ArgumentOutOfRangeException>(() =>
            NewSeries().AddValue(DateTimeOffset.UnixEpoch, 1m, (MeasurementStatus)99));

    [Fact]
    public void Repeated_local_hour_on_fall_back_day_maps_to_distinct_utc_intervals()
    {
        // 25 October 2026: 02:00–03:00 local time occurs twice (CEST, then CET).
        var series = NewSeries();
        var firstPass = new DateTimeOffset(2026, 10, 25, 2, 0, 0, TimeSpan.FromHours(2));
        var secondPass = new DateTimeOffset(2026, 10, 25, 2, 0, 0, TimeSpan.FromHours(1));
        Berlin.IsAmbiguousTime(firstPass).ShouldBeTrue();

        var a = series.AddValue(firstPass, 1m, MeasurementStatus.Measured);
        var b = series.AddValue(secondPass, 1m, MeasurementStatus.Measured);

        (b.IntervalStart - a.IntervalStart).ShouldBe(TimeSpan.FromHours(1));
    }

    [Fact]
    public void Spring_forward_day_has_92_quarter_hours_between_local_midnights()
    {
        // 29 March 2026: 02:00 jumps to 03:00 local time, so the day is 23 hours long.
        var start = new DateTimeOffset(2026, 3, 29, 0, 0, 0, Berlin.GetUtcOffset(new DateTime(2026, 3, 29)));
        var end = new DateTimeOffset(2026, 3, 30, 0, 0, 0, Berlin.GetUtcOffset(new DateTime(2026, 3, 30)));
        var series = NewSeries();

        for (var t = start; t < end; t += MeasurementSeries.IntervalLength)
        {
            series.AddValue(t, 0.1m, MeasurementStatus.Measured);
        }

        series.Values.Count.ShouldBe(92);
    }

    [Fact]
    public void Substitute_for_missing_interval_adds_replaced_value_with_trace()
    {
        var series = NewSeries();
        var start = new DateTimeOffset(2026, 7, 1, 12, 15, 0, TimeSpan.FromHours(2));

        var value = series.Substitute(start, 0.25m, ReplacementTrace.LinearInterpolation(0.2m, 0.3m));

        series.Values.ShouldHaveSingleItem().ShouldBeSameAs(value);
        value.IntervalStart.ShouldBe(start);
        value.Value.ShouldBe(0.25m);
        value.Status.ShouldBe(MeasurementStatus.Replaced);
        value.ReplacedBy.ShouldBe(ReplacementMethod.LinearInterpolation);
        value.AnchorValueBefore.ShouldBe(0.2m);
        value.AnchorValueAfter.ShouldBe(0.3m);
    }

    [Fact]
    public void Substitute_for_existing_interval_overwrites_it_instead_of_adding_a_duplicate()
    {
        var series = NewSeries();
        var start = new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);
        var spike = series.AddValue(start, 99m, MeasurementStatus.Measured);

        var value = series.Substitute(start, 0.5m, ReplacementTrace.LinearInterpolation(0.4m, 0.6m));

        value.ShouldBeSameAs(spike);
        series.Values.Count.ShouldBe(1);
        value.Value.ShouldBe(0.5m);
        value.Status.ShouldBe(MeasurementStatus.Replaced);
    }

    [Fact]
    public void Substitute_with_more_than_five_decimals_is_rejected() =>
        Should.Throw<ArgumentException>(() =>
            NewSeries().Substitute(DateTimeOffset.UnixEpoch, 0.123456m, ReplacementTrace.LinearInterpolation(0m, 1m)));

    [Fact]
    public void Sender_delivered_substitute_has_no_trace()
    {
        var value = NewSeries().AddValue(DateTimeOffset.UnixEpoch, 1m, MeasurementStatus.Replaced);

        value.ReplacedBy.ShouldBeNull();
        value.AnchorValueBefore.ShouldBeNull();
        value.AnchorValueAfter.ShouldBeNull();
    }

    private static MeasurementSeries NewSeries() =>
        new MarketLocation("41373559241", EnergyDirection.Consumption)
            .AddMeterLocation("DE0001234567890000000000000000001")
            .AddSeries("1-1:1.29.0");
}
