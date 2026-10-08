using MeterDataService.Application.GapFilling;
using MeterDataService.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace MeterDataService.Tests.Unit.Application;

public class GapFillingServiceTests
{
    private static readonly TimeSpan Quarter = MeasurementSeries.IntervalLength;
    private static readonly DateOnly Wednesday = new(2026, 7, 1);
    private static readonly DateOnly LastWednesday = Wednesday.AddDays(-7);

    private readonly GapFillingService _service = new(NullLogger<GapFillingService>.Instance);

    [Fact]
    public void Short_gap_is_interpolated_and_long_gap_is_filled_from_a_similar_day()
    {
        // 08:00–08:30 missing (2 intervals), 12:00–15:00 missing (12 intervals).
        var series = NewSeries();
        AddDay(series, LastWednesday);
        AddDay(series, Wednesday, skip: [.. Enumerable.Range(32, 2), .. Enumerable.Range(48, 12)]);

        var result = _service.Fill(series, Wednesday, Wednesday);

        result.Unfilled.ShouldBeEmpty();
        result.Replaced.Count.ShouldBe(14);
        result.Replaced.Take(2).ShouldAllBe(v => v.ReplacedBy == ReplacementMethod.LinearInterpolation);
        result.Replaced.Skip(2).ShouldAllBe(v => v.ReplacedBy == ReplacementMethod.SimilarDay && v.SourceDay == LastWednesday);
    }

    [Fact]
    public void Short_gap_is_interpolated_even_when_a_similar_day_exists()
    {
        // Order matters: with similar-day filling first, this gap would take last week's shape.
        var series = NewSeries();
        AddDay(series, LastWednesday);
        AddDay(series, Wednesday, skip: [40]);

        var replaced = _service.Fill(series, Wednesday, Wednesday).Replaced.ShouldHaveSingleItem();

        replaced.ReplacedBy.ShouldBe(ReplacementMethod.LinearInterpolation);
        replaced.Value.ShouldBe((ValueAt(Wednesday, 39) + ValueAt(Wednesday, 41)) / 2);
    }

    [Fact]
    public void Rejected_spike_in_a_short_gap_is_interpolated()
    {
        var series = NewSeries();
        AddDay(series, Wednesday);
        var spike = IntervalStart(Wednesday, 40);

        var result = _service.Fill(series, Wednesday, Wednesday, new HashSet<DateTimeOffset> { spike });

        result.Replaced.ShouldHaveSingleItem().IntervalStart.ShouldBe(spike);
        series.Values.Count.ShouldBe(GermanCalendar.QuarterHoursPerNormalDay);
    }

    [Fact]
    public void Long_gap_without_history_falls_back_to_estimated_zero()
    {
        var series = NewSeries();
        AddDay(series, Wednesday, skip: [.. Enumerable.Range(48, 12)]);

        var result = _service.Fill(series, Wednesday, Wednesday);

        result.Replaced.Count.ShouldBe(12);
        result.Replaced.ShouldAllBe(v => v.Status == MeasurementStatus.Estimated && v.Value == 0m);
    }

    [Fact]
    public void Edge_gap_without_anchor_after_is_left_to_similar_day_filling()
    {
        // The last two intervals of the day are missing and nothing follows: no second anchor to interpolate to.
        var series = NewSeries();
        AddDay(series, LastWednesday);
        AddDay(series, Wednesday, skip: [94, 95]);

        var result = _service.Fill(series, Wednesday, Wednesday);

        result.Replaced.ShouldAllBe(v => v.ReplacedBy == ReplacementMethod.SimilarDay);
        result.Replaced.Select(v => v.Value).ShouldBe([ValueAt(LastWednesday, 94), ValueAt(LastWednesday, 95)]);
    }

    [Fact]
    public void Complete_period_writes_nothing()
    {
        var series = NewSeries();
        AddDay(series, Wednesday);

        _service.Fill(series, Wednesday, Wednesday).Replaced.ShouldBeEmpty();
    }

    private static decimal ValueAt(DateOnly day, int index) => day.Day + (index / 1000m);

    private static DateTimeOffset IntervalStart(DateOnly day, int index) =>
        GermanCalendar.StartOfDayUtc(day) + (index * Quarter);

    private static void AddDay(MeasurementSeries series, DateOnly day, int[]? skip = null)
    {
        for (var i = 0; i < GermanCalendar.IntervalsIn(day); i++)
        {
            if (skip is null || !skip.Contains(i))
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
