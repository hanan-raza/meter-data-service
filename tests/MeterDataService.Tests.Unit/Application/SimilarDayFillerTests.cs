using MeterDataService.Application.GapFilling;
using MeterDataService.Domain;
using Shouldly;

namespace MeterDataService.Tests.Unit.Application;

public class SimilarDayFillerTests
{
    private static readonly TimeSpan Quarter = MeasurementSeries.IntervalLength;

    // Wednesday 1 July 2026; the gap is 12:00–15:00 local, 12 intervals.
    private static readonly DateOnly Wednesday = new(2026, 7, 1);
    private const int GapFrom = 48;
    private const int GapLength = 12;

    [Fact]
    public void Wednesday_gap_is_filled_from_the_previous_Wednesday()
    {
        Wednesday.DayOfWeek.ShouldBe(DayOfWeek.Wednesday);
        var lastWednesday = Wednesday.AddDays(-7);
        var series = SeriesWithDays(Wednesday.AddDays(-14), Wednesday.AddDays(-1));
        AddDay(series, Wednesday, skipFrom: GapFrom, skipCount: GapLength);

        var result = Fill(series, Wednesday);

        result.Unfilled.ShouldBeEmpty();
        result.Replaced.Count.ShouldBe(GapLength);
        result.Replaced.Select(v => v.Value)
            .ShouldBe(Enumerable.Range(GapFrom, GapLength).Select(i => ValueAt(lastWednesday, i)));
        result.Replaced.ShouldAllBe(v =>
            v.Status == MeasurementStatus.Replaced
            && v.ReplacedBy == ReplacementMethod.SimilarDay
            && v.SourceDay == lastWednesday
            && v.AnchorValueBefore == null);
    }

    [Fact]
    public void Same_weekday_is_preferred_over_a_more_recent_workday()
    {
        var series = SeriesWithDays(Wednesday.AddDays(-7), Wednesday.AddDays(-1));
        AddDay(series, Wednesday, skipFrom: GapFrom, skipCount: GapLength);

        Fill(series, Wednesday).Replaced.ShouldAllBe(v => v.SourceDay == Wednesday.AddDays(-7));
    }

    [Fact]
    public void Wednesday_two_weeks_back_is_used_when_last_Wednesday_has_a_hole_in_the_slice()
    {
        var series = NewSeries();
        AddDay(series, Wednesday.AddDays(-14));
        AddDay(series, Wednesday.AddDays(-7), skipFrom: GapFrom + 5, skipCount: 1);
        AddDay(series, Wednesday, skipFrom: GapFrom, skipCount: GapLength);

        Fill(series, Wednesday).Replaced.ShouldAllBe(v => v.SourceDay == Wednesday.AddDays(-14));
    }

    [Fact]
    public void Most_recent_other_workday_is_used_when_no_Wednesday_is_available()
    {
        var series = NewSeries();
        AddDay(series, Wednesday.AddDays(-2));
        AddDay(series, Wednesday.AddDays(-1));
        AddDay(series, Wednesday, skipFrom: GapFrom, skipCount: GapLength);

        Fill(series, Wednesday).Replaced.ShouldAllBe(v => v.SourceDay == Wednesday.AddDays(-1));
    }

    [Fact]
    public void Saturday_gap_is_filled_from_a_Saturday_not_from_the_Friday_before()
    {
        var saturday = new DateOnly(2026, 7, 4);
        saturday.DayOfWeek.ShouldBe(DayOfWeek.Saturday);
        var series = SeriesWithDays(saturday.AddDays(-7), saturday.AddDays(-1));
        AddDay(series, saturday, skipFrom: GapFrom, skipCount: GapLength);

        Fill(series, saturday).Replaced.ShouldAllBe(v => v.SourceDay == saturday.AddDays(-7));
    }

    [Fact]
    public void Without_a_similar_day_the_gap_is_set_to_zero_and_marked_estimated()
    {
        // Only weekend days in the lookback: wrong day type for a Wednesday.
        var series = NewSeries();
        AddDay(series, Wednesday.AddDays(-4));
        AddDay(series, Wednesday.AddDays(-3));
        AddDay(series, Wednesday, skipFrom: GapFrom, skipCount: GapLength);

        var result = Fill(series, Wednesday);

        result.Unfilled.ShouldBeEmpty();
        result.Replaced.Count.ShouldBe(GapLength);
        result.Replaced.ShouldAllBe(v =>
            v.Value == 0m
            && v.Status == MeasurementStatus.Estimated
            && v.ReplacedBy == ReplacementMethod.ZeroFallback
            && v.SourceDay == null);
    }

    [Fact]
    public void Day_beyond_the_lookback_is_not_used()
    {
        var series = NewSeries();
        AddDay(series, Wednesday.AddDays(-(SimilarDayFiller.LookbackDays + 1)));
        AddDay(series, Wednesday, skipFrom: GapFrom, skipCount: GapLength);

        Fill(series, Wednesday).Replaced.ShouldAllBe(v => v.ReplacedBy == ReplacementMethod.ZeroFallback);
    }

    [Fact]
    public void Rejected_value_on_the_candidate_day_disqualifies_it()
    {
        var lastWednesday = Wednesday.AddDays(-7);
        var series = SeriesWithDays(Wednesday.AddDays(-14), lastWednesday);
        AddDay(series, Wednesday, skipFrom: GapFrom, skipCount: GapLength);
        var rejected = new HashSet<DateTimeOffset> { IntervalStart(lastWednesday, GapFrom + 3) };

        var result = Fill(series, Wednesday, rejected);

        result.Replaced.ShouldAllBe(v => v.SourceDay == Wednesday.AddDays(-14));
    }

    [Fact]
    public void Substitute_on_the_candidate_day_disqualifies_it()
    {
        var lastWednesday = Wednesday.AddDays(-7);
        var series = SeriesWithDays(Wednesday.AddDays(-14), lastWednesday);
        series.Substitute(IntervalStart(lastWednesday, GapFrom), 1m, ReplacementTrace.LinearInterpolation(1m, 1m));
        AddDay(series, Wednesday, skipFrom: GapFrom, skipCount: GapLength);

        Fill(series, Wednesday).Replaced.ShouldAllBe(v => v.SourceDay == Wednesday.AddDays(-14));
    }

    [Fact]
    public void Gap_across_midnight_takes_each_part_from_its_own_similar_day()
    {
        // Wednesday 22:00 to Thursday 02:00 local.
        var thursday = Wednesday.AddDays(1);
        var series = SeriesWithDays(Wednesday.AddDays(-7), Wednesday.AddDays(-1));
        AddDay(series, Wednesday, skipFrom: 88, skipCount: 8);
        AddDay(series, thursday, skipFrom: 0, skipCount: 8);

        var result = SimilarDayFiller.Fill(series, GapDetector.FindGaps(series, Wednesday, thursday));

        result.Replaced.Count.ShouldBe(16);
        result.Replaced.Take(8).ShouldAllBe(v => v.SourceDay == Wednesday.AddDays(-7));
        result.Replaced.Skip(8).ShouldAllBe(v => v.SourceDay == thursday.AddDays(-7));
        result.Replaced[8].Value.ShouldBe(ValueAt(thursday.AddDays(-7), 0));
    }

    [Fact]
    public void Sender_estimate_is_kept_rather_than_overwritten_with_zero()
    {
        var series = NewSeries();
        AddDay(series, Wednesday, skipFrom: GapFrom, skipCount: GapLength);
        var estimate = series.AddValue(IntervalStart(Wednesday, GapFrom), 0.5m, MeasurementStatus.Estimated);

        var result = Fill(series, Wednesday);

        result.Replaced.Count.ShouldBe(GapLength - 1);
        result.Replaced.ShouldNotContain(estimate);
        estimate.Value.ShouldBe(0.5m);
        estimate.ReplacedBy.ShouldBeNull();
    }

    [Fact]
    public void Zero_fallback_is_replaced_once_a_similar_day_exists()
    {
        var series = NewSeries();
        AddDay(series, Wednesday, skipFrom: GapFrom, skipCount: GapLength);
        Fill(series, Wednesday);

        // Last Wednesday arrives later, e.g. from a re-delivery.
        AddDay(series, Wednesday.AddDays(-7));
        var result = Fill(series, Wednesday);

        result.Replaced.Count.ShouldBe(GapLength);
        result.Replaced.ShouldAllBe(v => v.Status == MeasurementStatus.Replaced && v.SourceDay == Wednesday.AddDays(-7));
        series.Values.Count.ShouldBe(2 * GermanCalendar.QuarterHoursPerNormalDay);
    }

    [Fact]
    public void Both_passes_of_the_repeated_hour_on_fall_back_day_are_filled_from_the_single_hour_a_week_earlier()
    {
        // Sunday 25 October 2026: local 02:00–03:00 occurs twice; Sunday 18 October has it once.
        var fallBack = new DateOnly(2026, 10, 25);
        var weekBefore = fallBack.AddDays(-7);
        var firstPass = new DateTimeOffset(2026, 10, 25, 2, 0, 0, TimeSpan.FromHours(2));
        var gapFrom = (int)((firstPass - GermanCalendar.StartOfDayUtc(fallBack)) / Quarter);
        var series = NewSeries();
        AddDay(series, weekBefore);
        AddDay(series, fallBack, skipFrom: gapFrom, skipCount: 8);

        var result = Fill(series, fallBack);

        var sourceHour = Enumerable.Range(8, 4).Select(i => ValueAt(weekBefore, i)).ToArray();
        result.Replaced.Select(v => v.Value).ShouldBe([.. sourceHour, .. sourceHour]);
        result.Replaced.ShouldAllBe(v => v.SourceDay == weekBefore);
    }

    [Fact]
    public void Repeated_hour_as_source_uses_the_pass_with_the_target_offset()
    {
        // Sunday 1 November 2026 is in CET, so the CET pass of 25 October's 02:00–03:00 is the match.
        var target = new DateOnly(2026, 11, 1);
        var fallBack = target.AddDays(-7);
        var series = NewSeries();
        AddDay(series, fallBack);
        AddDay(series, target, skipFrom: 8, skipCount: 4);

        var result = Fill(series, target);

        // On the 100-interval fall-back day, indexes 8–11 are the CEST pass and 12–15 the CET pass.
        result.Replaced.Select(v => v.Value).ShouldBe(Enumerable.Range(12, 4).Select(i => ValueAt(fallBack, i)));
    }

    [Fact]
    public void Spring_forward_day_cannot_be_the_source_for_the_hour_it_skips()
    {
        // Sunday 29 March 2026 has no local 02:00–03:00, so Sunday 5 April falls back to 22 March.
        var target = new DateOnly(2026, 4, 5);
        var series = NewSeries();
        AddDay(series, target.AddDays(-14));
        AddDay(series, target.AddDays(-7));
        AddDay(series, target, skipFrom: 8, skipCount: 4);

        var result = Fill(series, target);

        result.Replaced.ShouldAllBe(v => v.SourceDay == target.AddDays(-14));
        result.Replaced.Select(v => v.Value).ShouldBe(Enumerable.Range(8, 4).Select(i => ValueAt(target.AddDays(-14), i)));
    }

    private static GapFillResult Fill(MeasurementSeries series, DateOnly day, HashSet<DateTimeOffset>? rejected = null) =>
        SimilarDayFiller.Fill(series, GapDetector.FindGaps(series, day, day, rejected), rejected);

    // Distinct per day and interval, so assertions can tell which day a value was copied from.
    private static decimal ValueAt(DateOnly day, int index) => day.Day + (index / 1000m);

    private static DateTimeOffset IntervalStart(DateOnly day, int index) =>
        GermanCalendar.StartOfDayUtc(day) + (index * Quarter);

    private static MeasurementSeries SeriesWithDays(DateOnly first, DateOnly last)
    {
        var series = NewSeries();
        for (var day = first; day <= last; day = day.AddDays(1))
        {
            AddDay(series, day);
        }

        return series;
    }

    private static void AddDay(MeasurementSeries series, DateOnly day, int skipFrom = 0, int skipCount = 0)
    {
        for (var i = 0; i < GermanCalendar.IntervalsIn(day); i++)
        {
            if (i < skipFrom || i >= skipFrom + skipCount)
            {
                series.AddValue(IntervalStart(day, i), ValueAt(day, i), MeasurementStatus.Measured);
            }
        }
    }

    private static MeasurementSeries NewSeries() =>
        new MarketLocation("41373559241", EnergyDirection.Consumption)
            .AddMeterLocation("DE0001234567890000000000000000001")
            .AddSeries("1-1:1.29.0");
}
