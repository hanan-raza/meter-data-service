using MeterDataService.Application.GapFilling;
using MeterDataService.Application.Import;
using MeterDataService.Application.Synthetic;
using MeterDataService.Domain;
using MeterDataService.Tests.Unit.Application;
using Shouldly;

namespace MeterDataService.Tests.Unit.Dst;

/// <summary>
/// The fall-back day (Umstellung auf Winterzeit): on the last Sunday of October the clocks go back from 03:00 CEST
/// to 02:00 CET. The local hour 02:00–02:59 happens twice, first at +02:00 and then at +01:00, so the day has
/// 25 hours and 100 quarter hours, and a local time alone doesn't identify an interval.
/// </summary>
public class FallBackDayTests
{
    private static readonly DateOnly Day = new(2026, 10, 25);
    private static readonly TimeSpan Quarter = MeasurementSeries.IntervalLength;
    private static readonly TimeSpan WinterTime = TimeSpan.FromHours(1);
    private static readonly TimeSpan SummerTime = TimeSpan.FromHours(2);

    [Fact]
    public void Day_lasts_25_hours_with_100_quarter_hours()
    {
        GermanCalendar.IntervalsIn(Day).ShouldBe(100);
        (GermanCalendar.StartOfDayUtc(Day.AddDays(1)) - GermanCalendar.StartOfDayUtc(Day)).ShouldBe(TimeSpan.FromHours(25));
        DstImportPipeline.QuarterHoursOf(Day).Count.ShouldBe(100);
    }

    [Fact]
    public void Generated_series_has_100_intervals_with_the_repeated_hour_at_both_offsets()
    {
        var values = Generator().GenerateHousehold(Guid.NewGuid(), Day, Day).ToList();

        values.Count.ShouldBe(100);
        values.Select(v => v.IntervalStart).Distinct().Count().ShouldBe(100);
        values.Zip(values.Skip(1)).ShouldAllBe(pair => pair.Second.IntervalStart - pair.First.IntervalStart == Quarter);

        var repeatedHour = values.Select(v => GermanCalendar.ToGermanTime(v.IntervalStart)).Where(t => t.Hour == 2).ToList();
        repeatedHour.Select(t => t.Offset).ShouldBe([.. Enumerable.Repeat(SummerTime, 4), .. Enumerable.Repeat(WinterTime, 4)]);
        repeatedHour.Select(t => t.ToString("HH:mm", null)).ShouldBe(["02:00", "02:15", "02:30", "02:45", "02:00", "02:15", "02:30", "02:45"]);
    }

    [Fact]
    public void Repeated_local_times_are_two_distinct_intervals_not_duplicates()
    {
        var parsed = CsvImportParser.Parse(new StringReader(DstImportPipeline.LocalTimeCsv(LocalRows())));

        parsed.Errors.ShouldBeEmpty();
        parsed.Readings.Select(r => r.IntervalStart).ShouldBe(DstImportPipeline.QuarterHoursOf(Day));

        var report = new ValidationEngine().Validate(parsed.Readings, Day, Day);
        report.Values.ShouldAllBe(v => v.Result.Outcome == ValidationOutcome.Ok);
        report.MissingIntervals.ShouldBeEmpty();
        report.Findings.ShouldBeEmpty();
    }

    [Fact]
    public void Rows_with_explicit_offsets_are_unambiguous_in_any_order()
    {
        // Winter-time pass first: with offsets the row order no longer matters.
        var starts = DstImportPipeline.QuarterHoursOf(Day);
        var reordered = starts.Take(8).Concat(starts.Skip(12).Take(4)).Concat(starts.Skip(8).Take(4)).Concat(starts.Skip(16));

        var parsed = CsvImportParser.Parse(new StringReader(DstImportPipeline.OffsetCsv(reordered.Select(s => (s, 0.25m)))));

        parsed.Errors.ShouldBeEmpty();
        parsed.Readings[8].IntervalStart.ShouldBe(Local(2, 0, WinterTime));
        parsed.Readings[12].IntervalStart.ShouldBe(Local(2, 0, SummerTime));
        parsed.Readings.Select(r => r.IntervalStart).ShouldBe(starts, ignoreOrder: true);
        new ValidationEngine().Validate(parsed.Readings, Day, Day).Values
            .ShouldAllBe(v => v.Result.Outcome == ValidationOutcome.Ok);
    }

    [Fact]
    public void Same_offset_interval_delivered_twice_is_a_duplicate_and_the_first_value_wins()
    {
        var starts = DstImportPipeline.QuarterHoursOf(Day);
        var rows = starts.Select(s => (s, 0.25m)).Append((Local(2, 0, WinterTime), 0.9m));

        var parsed = CsvImportParser.Parse(new StringReader(DstImportPipeline.OffsetCsv(rows)));
        var report = new ValidationEngine().Validate(parsed.Readings, Day, Day);

        var duplicate = report.Values.Where(v => v.Result.Outcome == ValidationOutcome.Rejected).ShouldHaveSingleItem();
        duplicate.Result.Rule.ShouldBe(ValidationRule.DuplicateInterval);
        duplicate.Reading.LineNumber.ShouldBe(102);
        duplicate.Reading.Value.ShouldBe(0.9m);
        report.MissingIntervals.ShouldBeEmpty();
        report.Findings.ShouldBeEmpty();
    }

    [Fact]
    public void Local_time_appearing_a_third_time_is_a_duplicate_of_the_winter_time_pass()
    {
        // Order of appearance can only tell two passes apart; a third 02:00 maps onto the second one again.
        var rows = LocalRows().Append((Day.ToDateTime(new TimeOnly(2, 0)), 0.9m));

        var parsed = CsvImportParser.Parse(new StringReader(DstImportPipeline.LocalTimeCsv(rows)));
        var report = new ValidationEngine().Validate(parsed.Readings, Day, Day);

        parsed.Errors.ShouldBeEmpty();
        parsed.Readings[^1].IntervalStart.ShouldBe(Local(2, 0, WinterTime));
        var duplicate = report.Values.Where(v => v.Result.Outcome == ValidationOutcome.Rejected).ShouldHaveSingleItem();
        duplicate.Result.Rule.ShouldBe(ValidationRule.DuplicateInterval);
        duplicate.Reading.LineNumber.ShouldBe(102);
    }

    [Fact]
    public void Gap_in_the_second_pass_of_the_repeated_hour_is_found_at_its_winter_time_instants()
    {
        var missing = Enumerable.Range(0, 4).Select(i => Local(2, 0, WinterTime) + (i * Quarter)).ToList();
        var series = new FakeMeasurementSeriesRepository().Series;
        foreach (var start in DstImportPipeline.QuarterHoursOf(Day).Except(missing))
        {
            series.AddValue(start, 0.25m, MeasurementStatus.Measured);
        }

        var gap = GapDetector.FindGaps(series, Day, Day).ShouldHaveSingleItem();

        gap.From.ShouldBe(Local(2, 0, WinterTime));
        gap.To.ShouldBe(Local(3, 0, WinterTime));
        gap.IntervalStarts().ShouldBe(missing);
        gap.AnchorBefore.ShouldBe(0.25m); // 02:45 summer time, the last interval of the first pass
    }

    [Fact]
    public async Task Import_of_a_25_hour_day_in_local_time_completes_with_100_measured_values()
    {
        var generated = Generator().GenerateHousehold(Guid.NewGuid(), Day, Day).ToList();
        var csv = DstImportPipeline.LocalTimeCsv(
            generated.Select(v => (GermanCalendar.ToGermanTime(v.IntervalStart).DateTime, v.Value)));

        var (status, series) = await DstImportPipeline.ImportAsync(csv);

        status.State.ShouldBe(ImportJobState.Completed);
        var summary = status.Summary.ShouldNotBeNull();
        summary.RowsRead.ShouldBe(100);
        summary.ParseErrors.ShouldBe(0);
        summary.Accepted.ShouldBe(100);
        summary.Rejected.ShouldBe(0);
        summary.MissingIntervals.ShouldBe(0);
        summary.Findings.ShouldBe(0);
        summary.Substituted.ShouldBe(0);

        series.Values.Select(v => (v.IntervalStart, v.Value))
            .ShouldBe(generated.Select(v => (v.IntervalStart, v.Value)), ignoreOrder: true);
    }

    [Fact]
    public async Task Import_that_ignores_the_clock_change_gets_the_second_pass_filled_by_interpolation()
    {
        // 96 rows, each local time once: the repeated hour is read as its first (summer-time) pass,
        // so the winter-time pass is missing and gets substitute values.
        var midnight = Day.ToDateTime(TimeOnly.MinValue);
        var csv = DstImportPipeline.LocalTimeCsv(Enumerable.Range(0, 96).Select(q => (midnight.AddMinutes(15 * q), 0.25m)));

        var (status, series) = await DstImportPipeline.ImportAsync(csv);

        status.State.ShouldBe(ImportJobState.Completed);
        status.Summary.ShouldBe(new ImportSummary(
            RowsRead: 96, ParseErrors: 0, Accepted: 96, Rejected: 0, MissingIntervals: 4, Findings: 2, Substituted: 4,
            Anomalies: 0, QualityScore: 97.2m));
        series.Values.Count.ShouldBe(100);
        series.Values.Where(v => v.Status == MeasurementStatus.Replaced)
            .ShouldAllBe(v => v.ReplacedBy == ReplacementMethod.LinearInterpolation && v.Value == 0.25m);
        series.Values.Where(v => v.Status == MeasurementStatus.Replaced).Select(v => v.IntervalStart).Order()
            .ShouldBe(Enumerable.Range(0, 4).Select(i => Local(2, 0, WinterTime) + (i * Quarter)));
    }

    private static DateTimeOffset Local(int hour, int minute, TimeSpan offset) => new(2026, 10, 25, hour, minute, 0, offset);

    /// <summary>All 100 quarter hours as local clock times without offset; 02:00–02:45 appear twice.</summary>
    private static IEnumerable<(DateTime Local, decimal Value)> LocalRows() =>
        DstImportPipeline.QuarterHoursOf(Day).Select(start => (GermanCalendar.ToGermanTime(start).DateTime, 0.25m));

    private static SyntheticProfileGenerator Generator() =>
        new(new SyntheticProfileOptions { GapProbability = 0, SpikeProbability = 0, Seed = 1025 });
}
