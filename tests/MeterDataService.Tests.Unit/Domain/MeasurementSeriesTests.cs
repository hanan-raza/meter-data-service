using MeterDataService.Domain;
using Shouldly;

namespace MeterDataService.Tests.Unit.Domain;

public class MeasurementSeriesTests
{
    private static readonly DateTimeOffset Noon = new(2026, 7, 1, 12, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Annotate_attaches_a_finding_in_utc()
    {
        var series = NewSeries();

        var annotation = series.Annotate(Noon, AnomalyType.Spike, 0.99876m);

        series.Annotations.ShouldBe([annotation]);
        annotation.MeasurementSeriesId.ShouldBe(series.Id);
        annotation.IntervalStart.ShouldBe(Noon);
        annotation.IntervalStart.Offset.ShouldBe(TimeSpan.Zero);
        annotation.Type.ShouldBe(AnomalyType.Spike);
        annotation.Score.ShouldBe(0.99876m);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(0.123456)]
    public void Annotate_rejects_a_score_outside_0_to_1_or_finer_than_the_stored_scale(double score)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => NewSeries().Annotate(Noon, AnomalyType.Spike, (decimal)score));
    }

    [Fact]
    public void Annotate_rejects_an_unknown_type()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => NewSeries().Annotate(Noon, (AnomalyType)99, 0.5m));
    }

    [Fact]
    public void ClearAnnotations_removes_only_findings_inside_the_half_open_range()
    {
        var series = NewSeries();
        var before = series.Annotate(Noon.AddMinutes(-15), AnomalyType.Spike, 0.99m);
        series.Annotate(Noon, AnomalyType.Spike, 0.99m);
        series.Annotate(Noon, AnomalyType.ChangePoint, 0.96m);
        var atEnd = series.Annotate(Noon.AddHours(1), AnomalyType.Spike, 0.99m);

        var removed = series.ClearAnnotations(Noon, Noon.AddHours(1));

        removed.ShouldBe(2);
        series.Annotations.ShouldBe([before, atEnd], ignoreOrder: true);
    }

    [Fact]
    public void Record_adds_a_value_for_a_new_interval()
    {
        var series = NewSeries();

        var value = series.Record(Noon, 0.25m, MeasurementStatus.Measured);

        series.Values.ShouldBe([value]);
        value.IntervalStart.ShouldBe(Noon);
        value.Status.ShouldBe(MeasurementStatus.Measured);
    }

    [Fact]
    public void Record_overwrites_the_stored_value_of_the_same_interval()
    {
        var series = NewSeries();
        var first = series.Record(Noon, 0.25m, MeasurementStatus.Measured);

        // Same instant, written in UTC: still the same interval.
        var corrected = series.Record(Noon.ToUniversalTime(), 0.3m, MeasurementStatus.Estimated);

        corrected.ShouldBeSameAs(first);
        series.Values.Count.ShouldBe(1);
        corrected.Value.ShouldBe(0.3m);
        corrected.Status.ShouldBe(MeasurementStatus.Estimated);
    }

    [Fact]
    public void Record_over_a_substitute_drops_its_trace()
    {
        var series = NewSeries();
        series.Substitute(Noon, 0.2m, ReplacementTrace.LinearInterpolation(0.1m, 0.3m));

        var delivered = series.Record(Noon, 0.25m, MeasurementStatus.Measured);

        delivered.Status.ShouldBe(MeasurementStatus.Measured);
        delivered.ReplacedBy.ShouldBeNull();
        delivered.AnchorValueBefore.ShouldBeNull();
        delivered.AnchorValueAfter.ShouldBeNull();
        delivered.SourceDay.ShouldBeNull();
    }

    [Fact]
    public void Record_keeps_the_scale_invariant_when_overwriting()
    {
        var series = NewSeries();
        series.Record(Noon, 0.25m, MeasurementStatus.Measured);

        Should.Throw<ArgumentException>(() => series.Record(Noon, 0.123456m, MeasurementStatus.Measured));
    }

    [Theory]
    [InlineData(EnergyDirection.Consumption, "1-1:1.29.0")]
    [InlineData(EnergyDirection.Generation, "1-1:2.29.0")]
    public void Energy_obis_code_follows_the_direction(EnergyDirection direction, string expected) =>
        MeasurementSeries.EnergyObisCodeFor(direction).ShouldBe(expected);

    private static MeasurementSeries NewSeries() =>
        new MarketLocation("41373559241", EnergyDirection.Consumption)
            .AddMeterLocation("DE0001234567890000000000000000001")
            .AddSeries(MeasurementSeries.ConsumedActiveEnergy);
}
