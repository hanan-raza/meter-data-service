using MeterDataService.Application.Synthetic;
using MeterDataService.Domain;
using MeterDataService.Infrastructure.AnomalyDetection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace MeterDataService.Tests.Unit.Infrastructure;

public class AnomalyDetectorTests
{
    private static readonly TimeSpan Quarter = MeasurementSeries.IntervalLength;

    // Wednesday; the week before serves as history.
    private static readonly DateOnly Day = new(2026, 7, 8);
    private static readonly DateOnly FirstHistoryDay = Day.AddDays(-7);

    private static readonly SyntheticProfileOptions Clean = new() { GapProbability = 0, SpikeProbability = 0, Seed = 42 };

    private readonly AnomalyDetector _detector = new(NullLogger<AnomalyDetector>.Instance);

    [Fact]
    public void Series_with_known_spikes_produces_spike_annotations_at_those_intervals()
    {
        var spikes = new[] { IntervalStart(Day, 30), IntervalStart(Day, 70) };
        var series = CommercialSeries(transform: (start, value) => spikes.Contains(start) ? value * 6 : value);

        var annotations = _detector.Annotate(series, Day, Day);

        var spikeAnnotations = annotations.Where(a => a.Type == AnomalyType.Spike).ToList();
        spikeAnnotations.ShouldNotBeEmpty();
        spikeAnnotations.Select(a => a.IntervalStart).ShouldBe(spikes, ignoreOrder: true);
        spikeAnnotations.ShouldAllBe(a => a.Score >= 0.99m && a.Score <= 1m);
        series.Annotations.ShouldBe(annotations, ignoreOrder: true);
    }

    [Fact]
    public void Lasting_level_shift_produces_a_change_point_annotation_shortly_after_the_shift()
    {
        // From 12:00 the site draws three times its base load, e.g. a new production line.
        var shift = IntervalStart(Day, 48);
        var series = CommercialSeries(transform: (start, value) => start >= shift ? value * 3 : value);

        var changePoint = _detector.Annotate(series, Day, Day).First(a => a.Type == AnomalyType.ChangePoint);

        changePoint.IntervalStart.ShouldBeInRange(shift, shift + (8 * Quarter));
    }

    [Fact]
    public void Level_shift_in_a_household_profile_is_found_despite_the_daily_shape()
    {
        var shift = IntervalStart(Day, 72);
        var series = HouseholdSeries(FirstHistoryDay, Day, transform: (start, value) => start >= shift ? decimal.Round(value * 1.5m, MeasurementValue.Scale) : value);

        _detector.Annotate(series, Day, Day)
            .Where(a => a.Type == AnomalyType.ChangePoint)
            .ShouldContain(a => a.IntervalStart >= shift && a.IntervalStart <= shift + (8 * Quarter));
    }

    [Fact]
    public void Change_points_need_a_week_of_history()
    {
        // Same shift, but only three days before it: nothing to compare the period with.
        var shift = IntervalStart(Day, 48);
        var series = CommercialSeries(
            transform: (start, value) => start >= shift ? value * 3 : value,
            skip: start => start < GermanCalendar.StartOfDayUtc(Day.AddDays(-3)));

        _detector.Annotate(series, Day, Day).ShouldNotContain(a => a.Type == AnomalyType.ChangePoint);
    }

    [Fact]
    public void Week_after_the_spring_forward_day_is_compared_by_local_time()
    {
        // 2026-03-29 has 92 intervals. Comparing by UTC instant would shift the household shape by an hour
        // against the week before and report the ramps as level changes.
        var springForward = new DateOnly(2026, 3, 29);
        var series = HouseholdSeries(springForward.AddDays(-7), springForward.AddDays(7));

        _detector.Annotate(series, springForward, springForward.AddDays(7))
            .ShouldNotContain(a => a.Type == AnomalyType.ChangePoint);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Clean_household_raises_at_most_one_finding_in_three_weeks(int seed)
    {
        // Guards the tuning: noise alone must stay far below the anomaly level the quality score punishes.
        var series = HouseholdSeries(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 28), seed);

        _detector.Annotate(series, new DateOnly(2026, 1, 8), new DateOnly(2026, 1, 28)).Count.ShouldBeLessThanOrEqualTo(1);
    }

    [Fact]
    public void Clean_series_produces_no_annotations()
    {
        var series = CommercialSeries();

        _detector.Annotate(series, Day, Day).ShouldBeEmpty();
    }

    [Fact]
    public void Daily_shape_of_a_household_is_not_reported_as_spikes()
    {
        // Morning and evening peaks repeat every day, so the one-day history already contains them.
        var series = HouseholdSeries(FirstHistoryDay, Day);

        _detector.Annotate(series, Day, Day).Where(a => a.Type == AnomalyType.Spike).ShouldBeEmpty();
    }

    [Fact]
    public void Only_the_requested_period_is_annotated()
    {
        var historySpike = IntervalStart(Day.AddDays(-1), 40);
        var series = CommercialSeries(transform: (start, value) => start == historySpike ? value * 6 : value);

        _detector.Annotate(series, Day, Day).ShouldBeEmpty();
        series.Annotations.ShouldBeEmpty();
    }

    [Fact]
    public void Running_twice_replaces_the_findings_and_yields_identical_annotations()
    {
        var spike = IntervalStart(Day, 30);
        var series = CommercialSeries(transform: (start, value) => start == spike ? value * 6 : value);

        var first = _detector.Annotate(series, Day, Day).Select(a => (a.IntervalStart, a.Type, a.Score)).ToList();
        var second = _detector.Annotate(series, Day, Day).Select(a => (a.IntervalStart, a.Type, a.Score)).ToList();

        second.ShouldBe(first);
        series.Annotations.Count.ShouldBe(first.Count);
    }

    [Fact]
    public void Estimated_placeholders_are_not_reported_as_anomalies()
    {
        // A zero-fallback block from gap filling: a data gap, not a drop in load.
        var series = CommercialSeries(skip: start => start >= IntervalStart(Day, 40) && start < IntervalStart(Day, 60));
        for (var i = 40; i < 60; i++)
        {
            series.Substitute(IntervalStart(Day, i), 0m, ReplacementTrace.ZeroFallback());
        }

        _detector.Annotate(series, Day, Day).ShouldBeEmpty();
    }

    [Fact]
    public void Constant_series_produces_no_annotations()
    {
        // No variance at all, e.g. a fixed-value test delivery or a capped meter.
        var series = CommercialSeries(transform: (_, _) => 0.25m);

        _detector.Annotate(series, Day, Day).ShouldBeEmpty();
    }

    [Fact]
    public void Series_without_values_in_the_period_produces_no_annotations()
    {
        _detector.Annotate(NewSeries(), Day, Day).ShouldBeEmpty();
    }

    private static DateTimeOffset IntervalStart(DateOnly day, int index) =>
        GermanCalendar.StartOfDayUtc(day) + (index * Quarter);

    private static MeasurementSeries CommercialSeries(
        Func<DateTimeOffset, decimal, decimal>? transform = null,
        Func<DateTimeOffset, bool>? skip = null)
    {
        var series = NewSeries();
        foreach (var value in new SyntheticProfileGenerator(Clean).GenerateCommercial(series.Id, FirstHistoryDay, Day))
        {
            if (skip?.Invoke(value.IntervalStart) == true)
            {
                continue;
            }

            series.AddValue(value.IntervalStart, transform?.Invoke(value.IntervalStart, value.Value) ?? value.Value, value.Status);
        }

        return series;
    }

    private static MeasurementSeries HouseholdSeries(
        DateOnly firstDay, DateOnly lastDay, int seed = 42, Func<DateTimeOffset, decimal, decimal>? transform = null)
    {
        var series = NewSeries();
        var generator = new SyntheticProfileGenerator(Clean with { Seed = seed });
        foreach (var value in generator.GenerateHousehold(series.Id, firstDay, lastDay))
        {
            series.AddValue(value.IntervalStart, transform?.Invoke(value.IntervalStart, value.Value) ?? value.Value, value.Status);
        }

        return series;
    }

    private static MeasurementSeries NewSeries() =>
        new MarketLocation("41373559241", EnergyDirection.Consumption)
            .AddMeterLocation("DE0001234567890000000000000000001")
            .AddSeries(MeasurementSeries.ConsumedActiveEnergy);
}
