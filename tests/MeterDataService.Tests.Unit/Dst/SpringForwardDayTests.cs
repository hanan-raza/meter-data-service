using MeterDataService.Application.GapFilling;
using MeterDataService.Application.Import;
using MeterDataService.Application.Synthetic;
using MeterDataService.Domain;
using MeterDataService.Tests.Unit.Application;
using Shouldly;

namespace MeterDataService.Tests.Unit.Dst;

/// <summary>
/// The spring-forward day (Umstellung auf Sommerzeit): on the last Sunday of March the clocks jump from 02:00 CET
/// to 03:00 CEST. The local hour 02:00–02:59 never happens, so the day has 23 hours and 92 quarter hours.
/// </summary>
public class SpringForwardDayTests
{
    private static readonly DateOnly Day = new(2026, 3, 29);
    private static readonly TimeSpan Quarter = MeasurementSeries.IntervalLength;
    private static readonly TimeSpan WinterTime = TimeSpan.FromHours(1);
    private static readonly TimeSpan SummerTime = TimeSpan.FromHours(2);

    [Fact]
    public void Day_lasts_23_hours_with_92_quarter_hours()
    {
        GermanCalendar.IntervalsIn(Day).ShouldBe(92);
        (GermanCalendar.StartOfDayUtc(Day.AddDays(1)) - GermanCalendar.StartOfDayUtc(Day)).ShouldBe(TimeSpan.FromHours(23));
        DstImportPipeline.QuarterHoursOf(Day).Count.ShouldBe(92);
    }

    [Fact]
    public void Generated_series_has_92_contiguous_intervals_and_none_in_the_skipped_hour()
    {
        var values = Generator().GenerateHousehold(Guid.NewGuid(), Day, Day).ToList();

        values.Count.ShouldBe(92);
        values.Zip(values.Skip(1)).ShouldAllBe(pair => pair.Second.IntervalStart - pair.First.IntervalStart == Quarter);

        var localTimes = values.Select(v => GermanCalendar.ToGermanTime(v.IntervalStart)).ToList();
        localTimes.ShouldNotContain(t => t.Hour == 2);
        localTimes[7].ShouldBe(Local(1, 45));
        localTimes[8].ShouldBe(Local(3, 0));
    }

    [Fact]
    public void Complete_day_has_no_gaps_and_no_findings_because_the_skipped_hour_is_not_missing()
    {
        var series = SeriesWith(DstImportPipeline.QuarterHoursOf(Day));
        var readings = Readings(DstImportPipeline.QuarterHoursOf(Day));

        GapDetector.FindGaps(series, Day, Day).ShouldBeEmpty();

        var report = new ValidationEngine().Validate(readings, Day, Day);
        report.MissingIntervals.ShouldBeEmpty();
        report.Findings.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(1, 0)] // the last winter-time hour before the jump
    [InlineData(1, 30)] // straddles the jump: 01:30, 01:45, then 03:00, 03:15
    [InlineData(3, 0)] // the first summer-time hour after the jump
    public void Four_missing_intervals_at_the_clock_change_are_one_gap_of_one_real_hour(int hour, int minute)
    {
        var first = Local(hour, minute);
        var missing = Enumerable.Range(0, 4).Select(i => first + (i * Quarter)).ToHashSet();
        var delivered = DstImportPipeline.QuarterHoursOf(Day).Where(start => !missing.Contains(start)).ToList();

        var gap = GapDetector.FindGaps(SeriesWith(delivered), Day, Day).ShouldHaveSingleItem();

        gap.IntervalCount.ShouldBe(4);
        gap.From.ShouldBe(first);
        (gap.To - gap.From).ShouldBe(TimeSpan.FromHours(1));
        gap.IntervalStarts().ShouldBe(missing.Order());

        var report = new ValidationEngine().Validate(Readings(delivered), Day, Day);
        report.MissingIntervals.ShouldBe(missing.Order());
        // Findings are ordered by start; the day-level count finding starts at midnight.
        report.Findings.Select(f => f.Rule).ShouldBe([ValidationRule.DstIntervalCount, ValidationRule.MissingInterval]);
        report.Findings[0].Reason.ShouldBe("23-hour day 2026-03-29 has 88 intervals, expected 92.");
        report.Findings[1].Reason.ShouldStartWith("4 missing interval(s)");
    }

    [Fact]
    public void Gap_across_the_jump_is_interpolated_like_any_one_hour_gap()
    {
        // 01:30–03:15 local is missing; the anchors 01:15 (1.0 kWh) and 03:30 (2.0 kWh) are 75 real minutes apart,
        // not 135 as the wall clock suggests, so the line has five equal steps.
        DateTimeOffset[] missing = [Local(1, 30), Local(1, 45), Local(3, 0), Local(3, 15)];
        var series = SeriesWith(
            DstImportPipeline.QuarterHoursOf(Day).Except(missing),
            start => start < Local(1, 30) ? 1m : 2m);
        var gaps = GapDetector.FindGaps(series, Day, Day);

        var result = LinearInterpolationFiller.Fill(series, gaps);

        result.Unfilled.ShouldBeEmpty();
        result.Replaced.Select(v => (v.IntervalStart, v.Value)).ShouldBe(
        [
            (Local(1, 30), 1.2m),
            (Local(1, 45), 1.4m),
            (Local(3, 0), 1.6m),
            (Local(3, 15), 1.8m),
        ]);
    }

    [Fact]
    public void Local_timestamps_in_the_skipped_hour_are_rejected_as_nonexistent()
    {
        // A sender that ignores the clock change delivers 96 rows, including 02:00–02:45, which don't exist.
        var midnight = Day.ToDateTime(TimeOnly.MinValue);
        var rows = Enumerable.Range(0, 96).Select(q => (midnight.AddMinutes(15 * q), 0.25m));

        var result = CsvImportParser.Parse(new StringReader(DstImportPipeline.LocalTimeCsv(rows)));

        result.Readings.Count.ShouldBe(92);
        result.Errors.Count.ShouldBe(4);
        result.Errors.ShouldAllBe(e => e.Message.Contains("does not exist"));
        result.Errors.Select(e => e.LineNumber).ShouldBe([10, 11, 12, 13]);
        result.Readings.Select(r => r.IntervalStart).ShouldBe(DstImportPipeline.QuarterHoursOf(Day));
    }

    [Fact]
    public async Task Import_of_a_23_hour_day_in_local_time_completes_with_92_measured_values()
    {
        var generated = Generator().GenerateHousehold(Guid.NewGuid(), Day, Day).ToList();
        var csv = DstImportPipeline.LocalTimeCsv(
            generated.Select(v => (GermanCalendar.ToGermanTime(v.IntervalStart).DateTime, v.Value)));

        var (status, series) = await DstImportPipeline.ImportAsync(csv);

        status.State.ShouldBe(ImportJobState.Completed);
        var summary = status.Summary.ShouldNotBeNull();
        summary.RowsRead.ShouldBe(92);
        summary.ParseErrors.ShouldBe(0);
        summary.Accepted.ShouldBe(92);
        summary.MissingIntervals.ShouldBe(0);
        summary.Findings.ShouldBe(0);
        summary.Substituted.ShouldBe(0);

        series.Values.Select(v => (v.IntervalStart, v.Value))
            .ShouldBe(generated.Select(v => (v.IntervalStart, v.Value)), ignoreOrder: true);
        series.Values.ShouldAllBe(v => v.Status == MeasurementStatus.Measured);
    }

    [Fact]
    public async Task Import_that_ignores_the_clock_change_completes_without_the_nonexistent_hour()
    {
        var midnight = Day.ToDateTime(TimeOnly.MinValue);
        var csv = DstImportPipeline.LocalTimeCsv(Enumerable.Range(0, 96).Select(q => (midnight.AddMinutes(15 * q), 0.25m)));

        var (status, series) = await DstImportPipeline.ImportAsync(csv);

        status.State.ShouldBe(ImportJobState.Completed);
        status.Summary.ShouldBe(new ImportSummary(
            RowsRead: 92, ParseErrors: 4, Accepted: 92, Rejected: 0, MissingIntervals: 0, Findings: 0, Substituted: 0,
            Anomalies: 0, QualityScore: 100m));
        series.Values.Count.ShouldBe(92);
    }

    private static DateTimeOffset Local(int hour, int minute) =>
        new(2026, 3, 29, hour, minute, 0, hour < 2 ? WinterTime : SummerTime);

    private static SyntheticProfileGenerator Generator() =>
        new(new SyntheticProfileOptions { GapProbability = 0, SpikeProbability = 0, Seed = 329 });

    private static MeasurementSeries SeriesWith(IEnumerable<DateTimeOffset> starts, Func<DateTimeOffset, decimal>? valueAt = null)
    {
        var series = new FakeMeasurementSeriesRepository().Series;
        foreach (var start in starts)
        {
            series.AddValue(start, valueAt?.Invoke(start) ?? 0.25m, MeasurementStatus.Measured);
        }

        return series;
    }

    private static List<ImportedReading> Readings(IEnumerable<DateTimeOffset> starts) =>
        starts.Select((start, i) => new ImportedReading(i + 2, start, 0.25m, MeasurementStatus.Measured)).ToList();
}
