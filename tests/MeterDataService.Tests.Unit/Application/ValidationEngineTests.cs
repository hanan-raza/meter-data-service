using System.Globalization;
using System.Text;
using MeterDataService.Application.Import;
using MeterDataService.Application.Synthetic;
using MeterDataService.Domain;
using Shouldly;

namespace MeterDataService.Tests.Unit.Application;

public class ValidationEngineTests
{
    private static readonly DateOnly NormalDay = new(2026, 6, 10);
    private static readonly DateOnly SpringForward = new(2026, 3, 29);
    private static readonly DateOnly FallBack = new(2026, 10, 25);
    private static readonly ValidationEngine Engine = new();

    [Fact]
    public void Complete_plausible_day_has_no_findings()
    {
        var report = Engine.Validate(ParseDay(NormalDay));

        report.Values.Count.ShouldBe(96);
        report.Values.ShouldAllBe(v => v.Result == ValidationResult.Ok);
        report.MissingIntervals.ShouldBeEmpty();
        report.Findings.ShouldBeEmpty();
    }

    [Fact]
    public void Csv_with_known_spike_rejects_only_the_spike()
    {
        var spikeAt = Quarter(NormalDay, 40);
        var report = Engine.Validate(ParseDay(NormalDay, value: (start, v) => start == spikeAt ? v * 5 : v));

        var rejected = report.Values.Where(v => v.Result.Outcome == ValidationOutcome.Rejected).ShouldHaveSingleItem();
        rejected.Reading.IntervalStart.ShouldBe(spikeAt);
        rejected.Result.Rule.ShouldBe(ValidationRule.Spike);
        rejected.Result.Reason.ShouldNotBeNull().ShouldContain("median");
        report.Findings.ShouldBeEmpty();
    }

    [Fact]
    public void Value_at_exactly_three_times_the_median_is_not_a_spike()
    {
        var at = Quarter(NormalDay, 40);
        var report = Engine.Validate(ParseDay(NormalDay, value: (start, _) => start == at ? 3m : 1m));

        report.Values.ShouldAllBe(v => v.Result.Outcome == ValidationOutcome.Ok);
    }

    [Fact]
    public void Spike_is_judged_against_surrounding_hour_not_neighbouring_spike()
    {
        var first = Quarter(NormalDay, 40);
        var second = Quarter(NormalDay, 41);
        var report = Engine.Validate(ParseDay(NormalDay, value: (start, v) => start == first || start == second ? v * 6 : v));

        report.Values.Where(v => v.Result.Rule == ValidationRule.Spike).Select(v => v.Reading.IntervalStart)
            .ShouldBe([first, second]);
    }

    [Fact]
    public void Csv_with_gap_reports_each_missing_interval_and_one_finding_per_gap()
    {
        var gapStart = Quarter(NormalDay, 20);
        var gapEnd = Quarter(NormalDay, 23);
        var lastInterval = Quarter(NormalDay, 95);
        var readings = ParseDay(NormalDay, skip: start => (start >= gapStart && start < gapEnd) || start == lastInterval);

        var report = Engine.Validate(readings, NormalDay, NormalDay);

        report.MissingIntervals.Count.ShouldBe(4);
        report.MissingIntervals.ShouldBe([Quarter(NormalDay, 20), Quarter(NormalDay, 21), Quarter(NormalDay, 22), lastInterval]);
        report.Findings.ShouldAllBe(f => f.Rule == ValidationRule.MissingInterval && f.Outcome == ValidationOutcome.Warning);
        report.Findings.Select(f => (f.From, f.To)).ShouldBe(
        [
            (gapStart, gapEnd),
            (lastInterval, GermanCalendar.StartOfDayUtc(NormalDay.AddDays(1))),
        ]);
        report.Findings[0].Reason.ShouldStartWith("3 missing interval(s)");
        report.Values.ShouldAllBe(v => v.Result == ValidationResult.Ok);
    }

    [Fact]
    public void Gap_at_period_start_is_found_only_when_period_is_given()
    {
        var readings = ParseDay(NormalDay, skip: start => start < Quarter(NormalDay, 2));

        Engine.Validate(readings).MissingIntervals.Count.ShouldBe(2);
        Engine.Validate(readings, NormalDay, NormalDay.AddDays(1)).MissingIntervals.Count.ShouldBe(2 + 96);
    }

    [Fact]
    public void Negative_value_is_rejected()
    {
        var at = Quarter(NormalDay, 10);
        var report = Engine.Validate(ParseDay(NormalDay, value: (start, v) => start == at ? -v : v));

        var rejected = report.Values.Where(v => v.Result.Outcome == ValidationOutcome.Rejected).ShouldHaveSingleItem();
        rejected.Reading.IntervalStart.ShouldBe(at);
        rejected.Result.Rule.ShouldBe(ValidationRule.NegativeValue);
    }

    [Fact]
    public void Duplicate_interval_keeps_first_value_and_rejects_repeat()
    {
        var readings = ParseDay(NormalDay).ToList();
        readings.Add(readings[5] with { LineNumber = 98, Value = 7m });

        var report = Engine.Validate(readings);

        report.Values[5].Result.ShouldBe(ValidationResult.Ok);
        report.Values[^1].Result.Rule.ShouldBe(ValidationRule.DuplicateInterval);
        report.MissingIntervals.ShouldBeEmpty();
    }

    [Fact]
    public void Reading_outside_period_is_rejected()
    {
        var readings = ParseDay(NormalDay);

        var report = Engine.Validate(readings, NormalDay.AddDays(-1), NormalDay.AddDays(-1));

        report.Values.ShouldAllBe(v => v.Result.Rule == ValidationRule.OutsidePeriod);
        report.MissingIntervals.Count.ShouldBe(96);
    }

    [Theory]
    [InlineData(2026, 3, 29, 92)]
    [InlineData(2026, 10, 25, 100)]
    public void Complete_dst_day_has_no_findings(int year, int month, int day, int expected)
    {
        var report = Engine.Validate(ParseDay(new DateOnly(year, month, day)));

        report.Values.Count.ShouldBe(expected);
        report.Findings.ShouldBeEmpty();
    }

    [Fact]
    public void Fall_back_day_delivered_with_96_intervals_is_flagged()
    {
        // A sender that ignores the clock change writes 00:00–23:45 local once each: the repeated hour is lost.
        var midnight = FallBack.ToDateTime(TimeOnly.MinValue);
        var rows = Enumerable.Range(0, 96).Select(q => $"{midnight.AddMinutes(15 * q):yyyy-MM-ddTHH:mm},1.0,Measured");
        var readings = Parse(rows);

        var report = Engine.Validate(readings);

        report.MissingIntervals.Count.ShouldBe(4);
        var dst = report.Findings.Where(f => f.Rule == ValidationRule.DstIntervalCount).ShouldHaveSingleItem();
        dst.Outcome.ShouldBe(ValidationOutcome.Warning);
        dst.Reason.ShouldBe("25-hour day 2026-10-25 has 96 intervals, expected 100. The sender probably ignored the clock change.");
        dst.From.ShouldBe(GermanCalendar.StartOfDayUtc(FallBack));
    }

    [Fact]
    public void Spring_forward_day_with_gap_gets_dst_finding_with_counts()
    {
        var readings = ParseDay(SpringForward, skip: start => start == Quarter(SpringForward, 50));

        var report = Engine.Validate(readings);

        report.Findings.Select(f => f.Rule).ShouldBe([ValidationRule.DstIntervalCount, ValidationRule.MissingInterval]);
        report.Findings[0].Reason.ShouldBe("23-hour day 2026-03-29 has 91 intervals, expected 92.");
    }

    [Fact]
    public void Dst_rule_does_not_fire_on_normal_days()
    {
        var readings = ParseDay(NormalDay, skip: start => start == Quarter(NormalDay, 50));

        Engine.Validate(readings).Findings.ShouldAllBe(f => f.Rule == ValidationRule.MissingInterval);
    }

    [Fact]
    public void Pv_ramp_at_sunrise_and_zero_night_are_not_spikes()
    {
        var generator = new SyntheticProfileGenerator(new SyntheticProfileOptions { GapProbability = 0, SpikeProbability = 0, Seed = 7 });
        var values = generator.GeneratePhotovoltaic(Guid.CreateVersion7(), new DateOnly(2026, 3, 27), new DateOnly(2026, 4, 2));

        var report = Engine.Validate(ToReadings(values));

        report.Values.ShouldAllBe(v => v.Result.Outcome == ValidationOutcome.Ok);
        report.Findings.ShouldBeEmpty();
    }

    [Fact]
    public void Generated_spikes_in_household_and_commercial_series_are_all_rejected_and_nothing_else()
    {
        var first = new DateOnly(2026, 10, 19);
        var last = new DateOnly(2026, 10, 31);
        var clean = new SyntheticProfileOptions { GapProbability = 0, SpikeProbability = 0, Seed = 11 };
        var spiky = clean with { SpikeProbability = 0.01 };
        var seriesId = Guid.CreateVersion7();

        foreach (var generate in new Func<SyntheticProfileGenerator, IEnumerable<MeasurementValue>>[]
                 {
                     g => g.GenerateHousehold(seriesId, first, last),
                     g => g.GenerateCommercial(seriesId, first, last),
                 })
        {
            var baseline = generate(new SyntheticProfileGenerator(clean)).ToList();
            var disturbed = generate(new SyntheticProfileGenerator(spiky)).ToList();
            var injected = baseline.Zip(disturbed).Where(p => p.First.Value != p.Second.Value).Select(p => p.Second.IntervalStart).ToList();

            var report = Engine.Validate(ToReadings(disturbed));

            injected.ShouldNotBeEmpty();
            report.Values.Where(v => v.Result.Outcome != ValidationOutcome.Ok).Select(v => v.Reading.IntervalStart).ShouldBe(injected);
            report.Findings.ShouldBeEmpty();
        }
    }

    [Fact]
    public void Spike_factor_must_be_greater_than_one()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new ValidationEngine(1m));
    }

    [Fact]
    public void Period_must_not_end_before_it_starts()
    {
        Should.Throw<ArgumentException>(() => Engine.Validate([], NormalDay, NormalDay.AddDays(-1)));
    }

    private static DateTimeOffset Quarter(DateOnly day, int index) =>
        GermanCalendar.StartOfDayUtc(day) + index * MeasurementSeries.IntervalLength;

    /// <summary>Builds and parses a CSV for one local day with a smooth load around 1 kWh per interval.</summary>
    private static IReadOnlyList<ImportedReading> ParseDay(
        DateOnly day, Func<DateTimeOffset, bool>? skip = null, Func<DateTimeOffset, decimal, decimal>? value = null)
    {
        var rows = Enumerable.Range(0, GermanCalendar.IntervalsIn(day))
            .Select(q => Quarter(day, q))
            .Where(start => skip?.Invoke(start) != true)
            .Select(start =>
            {
                var smooth = 1m + start.Hour / 100m;
                var v = value?.Invoke(start, smooth) ?? smooth;
                return string.Create(CultureInfo.InvariantCulture, $"{start:yyyy-MM-ddTHH:mm:ssK},{v},Measured");
            });
        return Parse(rows);
    }

    private static IReadOnlyList<ImportedReading> Parse(IEnumerable<string> rows)
    {
        var csv = new StringBuilder("timestamp,value,status\n");
        foreach (var row in rows)
        {
            csv.Append(row).Append('\n');
        }

        var result = CsvImportParser.Parse(new StringReader(csv.ToString()));
        result.Errors.ShouldBeEmpty();
        return result.Readings;
    }

    private static List<ImportedReading> ToReadings(IEnumerable<MeasurementValue> values) =>
        values.Select((v, i) => new ImportedReading(i + 2, v.IntervalStart, v.Value, v.Status)).ToList();
}
