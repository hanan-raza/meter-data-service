using MeterDataService.Application.Quality;
using MeterDataService.Domain;
using Shouldly;

namespace MeterDataService.Tests.Unit.Application;

public class DataQualityScorerTests
{
    private static readonly TimeSpan Quarter = MeasurementSeries.IntervalLength;
    private static readonly DateOnly Day = new(2026, 7, 8);

    [Fact]
    public void Complete_metered_day_without_anomalies_scores_100()
    {
        var series = NewSeries();
        AddDay(series, Day);

        var score = DataQualityScorer.Score(series, Day, Day);

        score.ShouldBe(new DataQualityScore(100m, 96, 96, 0, 0, 0, 0));
    }

    [Fact]
    public void Mixed_day_scores_by_the_documented_weights()
    {
        // 80 metered, 10 replaced, 2 estimated, 4 missing, 2 anomalies.
        var series = NewSeries();
        AddDay(series, Day, count: 80);
        for (var i = 80; i < 90; i++)
        {
            series.Substitute(At(Day, i), 0.2m, ReplacementTrace.SimilarDay(Day.AddDays(-7)));
        }

        series.Substitute(At(Day, 90), 0m, ReplacementTrace.ZeroFallback());
        series.Substitute(At(Day, 91), 0m, ReplacementTrace.ZeroFallback());
        series.Annotate(At(Day, 10), AnomalyType.Spike, 0.999m);
        series.Annotate(At(Day, 50), AnomalyType.ChangePoint, 0.97m);

        var score = DataQualityScorer.Score(series, Day, Day);

        // 70 × 80/96 + 20 × 10/16 + 10 × 90/96 × (1 − 2/4) = 58.333… + 12.5 + 4.6875
        score.ShouldBe(new DataQualityScore(75.52m, 96, 80, 10, 2, 4, 2));
    }

    [Fact]
    public void Spring_forward_day_is_complete_with_92_intervals()
    {
        var springForward = new DateOnly(2026, 3, 29);
        var series = NewSeries();
        AddDay(series, springForward);

        var score = DataQualityScorer.Score(series, springForward, springForward);

        score.ExpectedIntervals.ShouldBe(92);
        score.Score.ShouldBe(100m);
    }

    [Fact]
    public void Fall_back_day_expects_100_intervals()
    {
        var fallBack = new DateOnly(2026, 10, 25);
        var series = NewSeries();
        AddDay(series, fallBack, count: 96);

        var score = DataQualityScorer.Score(series, fallBack, fallBack);

        score.ExpectedIntervals.ShouldBe(100);
        score.Missing.ShouldBe(4);
        score.Score.ShouldBeLessThan(100m);
    }

    [Fact]
    public void Anomaly_points_never_go_negative()
    {
        var series = NewSeries();
        AddDay(series, Day);
        for (var i = 0; i < 10; i++)
        {
            series.Annotate(At(Day, i * 5), AnomalyType.Spike, 0.995m);
        }

        DataQualityScorer.Score(series, Day, Day).Score.ShouldBe(90m);
    }

    [Fact]
    public void Anomalies_are_weighed_per_day()
    {
        // One anomaly over two days costs half of what it costs in one: 10 × (1 − 0.5/4).
        var series = NewSeries();
        AddDay(series, Day);
        AddDay(series, Day.AddDays(1));
        series.Annotate(At(Day, 40), AnomalyType.Spike, 0.995m);

        DataQualityScorer.Score(series, Day, Day.AddDays(1)).Score.ShouldBe(98.75m);
    }

    [Fact]
    public void Empty_day_scores_zero()
    {
        DataQualityScorer.Score(NewSeries(), Day, Day).ShouldBe(new DataQualityScore(0m, 96, 0, 0, 0, 96, 0));
    }

    [Fact]
    public void Values_and_annotations_outside_the_period_are_ignored()
    {
        var series = NewSeries();
        AddDay(series, Day.AddDays(-1), count: 10);
        AddDay(series, Day);
        series.Annotate(At(Day.AddDays(1), 0), AnomalyType.Spike, 0.995m);

        DataQualityScorer.Score(series, Day, Day).ShouldBe(new DataQualityScore(100m, 96, 96, 0, 0, 0, 0));
    }

    [Fact]
    public void Same_input_yields_the_same_score_regardless_of_value_order()
    {
        var forward = NewSeries();
        var backward = NewSeries();
        for (var i = 0; i < 90; i++)
        {
            forward.AddValue(At(Day, i), 0.25m, MeasurementStatus.Measured);
            backward.AddValue(At(Day, 89 - i), 0.25m, MeasurementStatus.Measured);
        }

        forward.Annotate(At(Day, 3), AnomalyType.Spike, 0.995m);
        backward.Annotate(At(Day, 3), AnomalyType.Spike, 0.995m);

        DataQualityScorer.Score(forward, Day, Day).ShouldBe(DataQualityScorer.Score(backward, Day, Day));
        DataQualityScorer.Score(forward, Day, Day).ShouldBe(DataQualityScorer.Score(forward, Day, Day));
    }

    [Fact]
    public void Last_day_before_first_day_is_rejected()
    {
        Should.Throw<ArgumentException>(() => DataQualityScorer.Score(NewSeries(), Day, Day.AddDays(-1)));
    }

    private static DateTimeOffset At(DateOnly day, int index) => GermanCalendar.StartOfDayUtc(day) + (index * Quarter);

    private static void AddDay(MeasurementSeries series, DateOnly day, int? count = null)
    {
        for (var i = 0; i < (count ?? GermanCalendar.IntervalsIn(day)); i++)
        {
            series.AddValue(At(day, i), 0.25m, MeasurementStatus.Measured);
        }
    }

    private static MeasurementSeries NewSeries() =>
        new MarketLocation("41373559241", EnergyDirection.Consumption)
            .AddMeterLocation("DE0001234567890000000000000000001")
            .AddSeries(MeasurementSeries.ConsumedActiveEnergy);
}
