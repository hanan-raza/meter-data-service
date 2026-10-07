using MeterDataService.Application.GapFilling;
using MeterDataService.Domain;
using Shouldly;

namespace MeterDataService.Tests.Unit.Application;

public class LinearInterpolationFillerTests
{
    private static readonly TimeSpan Quarter = MeasurementSeries.IntervalLength;
    private static readonly DateTimeOffset T0 = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

    public static TheoryData<int, decimal[]> ShortGaps => new()
    {
        { 1, [1.5m] },
        { 2, [1.33333m, 1.66667m] },
        { 3, [1.25m, 1.5m, 1.75m] },
        { 4, [1.2m, 1.4m, 1.6m, 1.8m] },
    };

    [Theory]
    [MemberData(nameof(ShortGaps))]
    public void Short_gap_is_filled_on_the_line_between_its_anchors(int length, decimal[] expected)
    {
        // Anchor 1.0 kWh at 10:00, gap, anchor 2.0 kWh right after it.
        var series = NewSeries();
        series.AddValue(T0, 1m, MeasurementStatus.Measured);
        series.AddValue(T0 + ((length + 1) * Quarter), 2m, MeasurementStatus.Measured);

        var result = Fill(series, T0, T0 + ((length + 2) * Quarter));

        result.Unfilled.ShouldBeEmpty();
        result.Replaced.Select(v => v.Value).ShouldBe(expected);
        result.Replaced.Select(v => v.IntervalStart).ShouldBe(Enumerable.Range(1, length).Select(i => T0 + (i * Quarter)));
        series.Values.Count.ShouldBe(length + 2);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void Every_filled_value_is_marked_replaced_and_stores_algorithm_and_anchors(int length)
    {
        var series = SeriesWithGap(length, before: 0.8m, after: 0.4m);

        var result = Fill(series, T0, T0 + ((length + 2) * Quarter));

        result.Replaced.Count.ShouldBe(length);
        result.Replaced.ShouldAllBe(v =>
            v.Status == MeasurementStatus.Replaced
            && v.ReplacedBy == ReplacementMethod.LinearInterpolation
            && v.AnchorValueBefore == 0.8m
            && v.AnchorValueAfter == 0.4m);
    }

    [Fact]
    public void Gap_of_five_intervals_is_not_filled()
    {
        var series = SeriesWithGap(5, before: 1m, after: 2m);

        var result = Fill(series, T0, T0 + (7 * Quarter));

        result.Replaced.ShouldBeEmpty();
        result.Unfilled.ShouldHaveSingleItem().IntervalCount.ShouldBe(5);
        series.Values.Count.ShouldBe(2);
    }

    [Fact]
    public void Gap_without_anchor_after_it_is_not_extrapolated()
    {
        var series = NewSeries();
        series.AddValue(T0, 1m, MeasurementStatus.Measured);

        var result = Fill(series, T0, T0 + (3 * Quarter));

        result.Replaced.ShouldBeEmpty();
        result.Unfilled.ShouldHaveSingleItem().AnchorAfter.ShouldBeNull();
    }

    [Fact]
    public void Rejected_spike_is_overwritten_in_place()
    {
        var series = NewSeries();
        series.AddValue(T0, 0.4m, MeasurementStatus.Measured);
        var spike = series.AddValue(T0 + Quarter, 9.9m, MeasurementStatus.Measured);
        series.AddValue(T0 + (2 * Quarter), 0.6m, MeasurementStatus.Measured);
        var gaps = GapDetector.FindGaps(series, T0, T0 + (3 * Quarter), new HashSet<DateTimeOffset> { spike.IntervalStart });

        var result = LinearInterpolationFiller.Fill(series, gaps);

        result.Replaced.ShouldHaveSingleItem().ShouldBeSameAs(spike);
        spike.Value.ShouldBe(0.5m);
        spike.Status.ShouldBe(MeasurementStatus.Replaced);
        series.Values.Count.ShouldBe(3);
    }

    [Fact]
    public void Only_short_gaps_are_filled_when_short_and_long_gaps_are_mixed()
    {
        // 10:15 missing (short), 11:00–12:15 missing (6 intervals, long).
        var series = NewSeries();
        foreach (var i in new[] { 0, 2, 3, 10 })
        {
            series.AddValue(T0 + (i * Quarter), 1m, MeasurementStatus.Measured);
        }

        var result = Fill(series, T0, T0 + (11 * Quarter));

        result.Replaced.ShouldHaveSingleItem().IntervalStart.ShouldBe(T0 + Quarter);
        result.Unfilled.ShouldHaveSingleItem().From.ShouldBe(T0 + (4 * Quarter));
    }

    [Fact]
    public void Midpoint_is_rounded_away_from_zero_to_five_decimals()
    {
        var series = SeriesWithGap(1, before: 0m, after: 0.00001m);

        var result = Fill(series, T0, T0 + (3 * Quarter));

        result.Replaced.Single().Value.ShouldBe(0.00001m);
    }

    [Fact]
    public void Gap_across_the_repeated_hour_on_fall_back_day_is_interpolated_in_real_time()
    {
        // 25 October 2026: 02:30 CEST, 02:45 CEST, 02:00 CET, 02:15 CET are four consecutive quarter-hours.
        var before = new DateTimeOffset(2026, 10, 25, 2, 15, 0, TimeSpan.FromHours(2));
        var after = new DateTimeOffset(2026, 10, 25, 2, 30, 0, TimeSpan.FromHours(1));
        var series = NewSeries();
        series.AddValue(before, 0.2m, MeasurementStatus.Measured);
        series.AddValue(after, 0.7m, MeasurementStatus.Measured);

        var result = Fill(series, before, after + Quarter);

        result.Replaced.Select(v => v.Value).ShouldBe([0.3m, 0.4m, 0.5m, 0.6m]);
        result.Replaced[^1].IntervalStart.ShouldBe(new DateTimeOffset(2026, 10, 25, 2, 15, 0, TimeSpan.FromHours(1)));
    }

    private static GapFillResult Fill(MeasurementSeries series, DateTimeOffset from, DateTimeOffset to) =>
        LinearInterpolationFiller.Fill(series, GapDetector.FindGaps(series, from, to));

    private static MeasurementSeries SeriesWithGap(int length, decimal before, decimal after)
    {
        var series = NewSeries();
        series.AddValue(T0, before, MeasurementStatus.Measured);
        series.AddValue(T0 + ((length + 1) * Quarter), after, MeasurementStatus.Measured);
        return series;
    }

    private static MeasurementSeries NewSeries() =>
        new MarketLocation("41373559241", EnergyDirection.Consumption)
            .AddMeterLocation("DE0001234567890000000000000000001")
            .AddSeries("1-1:1.29.0");
}
