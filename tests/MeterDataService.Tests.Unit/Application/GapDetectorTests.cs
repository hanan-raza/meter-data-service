using MeterDataService.Application.GapFilling;
using MeterDataService.Domain;
using Shouldly;

namespace MeterDataService.Tests.Unit.Application;

public class GapDetectorTests
{
    private static readonly TimeSpan Quarter = MeasurementSeries.IntervalLength;
    private static readonly DateTimeOffset T0 = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Complete_series_has_no_gaps()
    {
        var series = SeriesWith(T0, 8);

        GapDetector.FindGaps(series, T0, T0 + 8 * Quarter).ShouldBeEmpty();
    }

    [Fact]
    public void Missing_interval_is_a_gap_with_both_anchors()
    {
        var series = SeriesWith(T0, 8, skip: [3]);

        var gap = GapDetector.FindGaps(series, T0, T0 + 8 * Quarter).ShouldHaveSingleItem();

        gap.From.ShouldBe(T0 + 3 * Quarter);
        gap.To.ShouldBe(T0 + 4 * Quarter);
        gap.IntervalCount.ShouldBe(1);
        gap.AnchorBefore.ShouldBe(ValueAt(2));
        gap.AnchorAfter.ShouldBe(ValueAt(4));
    }

    [Fact]
    public void Rejected_value_is_a_gap_even_though_the_series_contains_it()
    {
        var series = SeriesWith(T0, 8);

        var gap = GapDetector.FindGaps(series, T0, T0 + 8 * Quarter, Rejected(5)).ShouldHaveSingleItem();

        gap.From.ShouldBe(T0 + 5 * Quarter);
        gap.IntervalCount.ShouldBe(1);
    }

    [Fact]
    public void Adjacent_missing_and_rejected_intervals_form_one_gap()
    {
        var series = SeriesWith(T0, 10, skip: [3, 4]);

        var gap = GapDetector.FindGaps(series, T0, T0 + 10 * Quarter, Rejected(5, 6)).ShouldHaveSingleItem();

        gap.From.ShouldBe(T0 + 3 * Quarter);
        gap.To.ShouldBe(T0 + 7 * Quarter);
        gap.IntervalCount.ShouldBe(4);
        gap.IntervalStarts().ShouldBe([T0 + 3 * Quarter, T0 + 4 * Quarter, T0 + 5 * Quarter, T0 + 6 * Quarter]);
        gap.AnchorBefore.ShouldBe(ValueAt(2));
        gap.AnchorAfter.ShouldBe(ValueAt(7));
    }

    [Fact]
    public void Separate_gaps_are_reported_in_time_order()
    {
        var series = SeriesWith(T0, 10, skip: [6, 1, 2]);

        var gaps = GapDetector.FindGaps(series, T0, T0 + 10 * Quarter);

        gaps.Select(g => (g.From, g.IntervalCount)).ShouldBe([(T0 + Quarter, 2), (T0 + 6 * Quarter, 1)]);
    }

    [Fact]
    public void Gaps_at_period_edges_have_no_anchor_on_the_open_side()
    {
        var series = SeriesWith(T0, 8, skip: [0, 1, 7]);

        var gaps = GapDetector.FindGaps(series, T0, T0 + 8 * Quarter);

        gaps.Count.ShouldBe(2);
        gaps[0].AnchorBefore.ShouldBeNull();
        gaps[0].AnchorAfter.ShouldBe(ValueAt(2));
        gaps[1].AnchorBefore.ShouldBe(ValueAt(6));
        gaps[1].AnchorAfter.ShouldBeNull();
        gaps[1].To.ShouldBe(T0 + 8 * Quarter);
    }

    [Fact]
    public void Anchor_just_outside_the_period_is_used()
    {
        // The import covers 10:15–12:00; the 10:00 value came from an earlier import.
        var series = SeriesWith(T0, 8, skip: [1]);

        var gap = GapDetector.FindGaps(series, T0 + Quarter, T0 + 8 * Quarter).ShouldHaveSingleItem();

        gap.AnchorBefore.ShouldBe(ValueAt(0));
    }

    [Fact]
    public void Rejected_anchor_outside_the_period_is_not_used()
    {
        var series = SeriesWith(T0, 8, skip: [1]);

        var gap = GapDetector.FindGaps(series, T0 + Quarter, T0 + 8 * Quarter, Rejected(0)).ShouldHaveSingleItem();

        gap.AnchorBefore.ShouldBeNull();
    }

    [Fact]
    public void Empty_series_is_one_gap_over_the_whole_day()
    {
        var day = new DateOnly(2026, 7, 1);

        var gap = GapDetector.FindGaps(NewSeries(), day, day).ShouldHaveSingleItem();

        gap.IntervalCount.ShouldBe(96);
        gap.From.ShouldBe(new DateTimeOffset(2026, 6, 30, 22, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Complete_spring_forward_day_with_92_intervals_has_no_gaps()
    {
        var day = new DateOnly(2026, 3, 29);
        var series = SeriesWith(GermanCalendar.StartOfDayUtc(day), 92);

        GapDetector.FindGaps(series, day, day).ShouldBeEmpty();
    }

    [Fact]
    public void Gap_over_the_repeated_hour_on_fall_back_day_counts_both_passes()
    {
        // 25 October 2026: local 02:00–03:00 occurs twice. Both passes are missing: 2 hours, 8 intervals.
        var day = new DateOnly(2026, 10, 25);
        var dayStart = GermanCalendar.StartOfDayUtc(day);
        var firstPass = new DateTimeOffset(2026, 10, 25, 2, 0, 0, TimeSpan.FromHours(2));
        var skip = Enumerable.Range((int)((firstPass - dayStart) / Quarter), 8).ToArray();
        var series = SeriesWith(dayStart, 100, skip);

        var gap = GapDetector.FindGaps(series, day, day).ShouldHaveSingleItem();

        gap.From.ShouldBe(firstPass);
        gap.To.ShouldBe(new DateTimeOffset(2026, 10, 25, 3, 0, 0, TimeSpan.FromHours(1)));
        gap.IntervalCount.ShouldBe(8);
    }

    [Fact]
    public void Period_must_be_on_quarter_hours() =>
        Should.Throw<ArgumentException>(() => GapDetector.FindGaps(NewSeries(), T0.AddMinutes(5), T0 + Quarter));

    [Fact]
    public void Period_end_must_be_after_start() =>
        Should.Throw<ArgumentException>(() => GapDetector.FindGaps(NewSeries(), T0, T0));

    // Distinct values per interval, so anchor assertions can tell neighbours apart.
    private static decimal ValueAt(int index) => 1m + (index / 100m);

    private static MeasurementSeries SeriesWith(DateTimeOffset start, int count, int[]? skip = null)
    {
        var series = NewSeries();
        for (var i = 0; i < count; i++)
        {
            if (skip is null || !skip.Contains(i))
            {
                series.AddValue(start + (i * Quarter), ValueAt(i), MeasurementStatus.Measured);
            }
        }

        return series;
    }

    private static HashSet<DateTimeOffset> Rejected(params int[] indexes) =>
        indexes.Select(i => T0 + (i * Quarter)).ToHashSet();

    private static MeasurementSeries NewSeries() =>
        new MarketLocation("41373559241", EnergyDirection.Consumption)
            .AddMeterLocation("DE0001234567890000000000000000001")
            .AddSeries("1-1:1.29.0");
}
