using MeterDataService.Application.Synthetic;
using MeterDataService.Domain;
using Shouldly;

namespace MeterDataService.Tests.Unit.Application;

public class SyntheticProfileGeneratorTests
{
    public enum Profile
    {
        Household,
        Commercial,
        Photovoltaic,
    }

    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    private static readonly Guid SeriesId = Guid.CreateVersion7();
    private static readonly SyntheticProfileOptions NoDefects = new() { GapProbability = 0, SpikeProbability = 0, Seed = 42 };

    public static TheoryData<Profile, DateOnly, int> DayLengths()
    {
        var data = new TheoryData<Profile, DateOnly, int>();
        foreach (var profile in Enum.GetValues<Profile>())
        {
            data.Add(profile, new DateOnly(2026, 6, 10), 96);
            // 29 March 2026: clocks jump from 02:00 to 03:00, a 23-hour day.
            data.Add(profile, new DateOnly(2026, 3, 29), 92);
            // 25 October 2026: 02:00–03:00 is repeated, a 25-hour day.
            data.Add(profile, new DateOnly(2026, 10, 25), 100);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(DayLengths))]
    public void Local_day_has_one_value_per_quarter_hour_including_dst_days(Profile profile, DateOnly day, int expected)
    {
        var values = Generate(profile, day, day, NoDefects);

        values.Count.ShouldBe(expected);
        values.Select(v => v.IntervalStart).Distinct().Count().ShouldBe(expected);
        values[0].IntervalStart.ShouldBe(LocalMidnight(day));
        values[^1].IntervalEnd.ShouldBe(LocalMidnight(day.AddDays(1)));
    }

    [Theory]
    [InlineData(Profile.Household)]
    [InlineData(Profile.Commercial)]
    [InlineData(Profile.Photovoltaic)]
    public void Multi_day_range_is_contiguous_across_dst_switch(Profile profile)
    {
        var values = Generate(profile, new DateOnly(2026, 3, 28), new DateOnly(2026, 3, 30), NoDefects);

        values.Count.ShouldBe(96 + 92 + 96);
        values.Zip(values.Skip(1)).ShouldAllBe(pair => pair.First.IntervalEnd == pair.Second.IntervalStart);
        values.ShouldAllBe(v => v.Status == MeasurementStatus.Measured && v.MeasurementSeriesId == SeriesId);
    }

    [Fact]
    public void Same_seed_produces_identical_series()
    {
        var options = new SyntheticProfileOptions { Seed = 7 };
        var day = new DateOnly(2026, 1, 15);

        var first = Generate(Profile.Household, day, day.AddDays(6), options);
        var second = Generate(Profile.Household, day, day.AddDays(6), options);

        second.Select(v => (v.IntervalStart, v.Value)).ShouldBe(first.Select(v => (v.IntervalStart, v.Value)));
    }

    [Fact]
    public void Default_gap_probability_removes_about_half_a_percent_of_a_year()
    {
        var options = new SyntheticProfileOptions { SpikeProbability = 0, Seed = 1 };
        const int intervalsIn2026 = 365 * 96;

        var values = Generate(Profile.Commercial, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), options);

        var missingShare = (double)(intervalsIn2026 - values.Count) / intervalsIn2026;
        missingShare.ShouldBeInRange(0.004, 0.006);
    }

    [Fact]
    public void Spikes_multiply_the_undisturbed_value_and_keep_measured_status()
    {
        var day = new DateOnly(2026, 2, 3);
        var clean = Generate(Profile.Household, day, day, NoDefects);
        var spiked = Generate(Profile.Household, day, day, NoDefects with { SpikeProbability = 1 });

        spiked.Count.ShouldBe(clean.Count);
        foreach (var (original, spike) in clean.Zip(spiked))
        {
            spike.IntervalStart.ShouldBe(original.IntervalStart);
            spike.Status.ShouldBe(MeasurementStatus.Measured);
            (spike.Value / original.Value).ShouldBeInRange(3.99m, 8.01m);
        }
    }

    [Fact]
    public void Default_spike_probability_injects_a_few_outliers_per_year()
    {
        var options = new SyntheticProfileOptions { GapProbability = 0, Seed = 3 };
        var range = (new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        var clean = Generate(Profile.Commercial, range.Item1, range.Item2, NoDefects with { Seed = 3 });
        var withSpikes = Generate(Profile.Commercial, range.Item1, range.Item2, options);

        var spikeShare = (double)clean.Zip(withSpikes).Count(p => p.Second.Value > p.First.Value * 3) / clean.Count;
        spikeShare.ShouldBeInRange(0.001, 0.003);
    }

    [Fact]
    public void Household_year_matches_requested_annual_consumption()
    {
        var values = new SyntheticProfileGenerator(NoDefects)
            .GenerateHousehold(SeriesId, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), annualConsumptionKwh: 4_000m)
            .ToList();

        values.Sum(v => v.Value).ShouldBeInRange(3_900m, 4_100m);
    }

    [Fact]
    public void Household_evening_peak_exceeds_night_load_and_winter_exceeds_summer()
    {
        var january = Generate(Profile.Household, new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9), NoDefects);
        var july = Generate(Profile.Household, new DateOnly(2026, 7, 6), new DateOnly(2026, 7, 10), NoDefects);

        MeanAtLocalHours(january, 18, 20).ShouldBeGreaterThan(2 * MeanAtLocalHours(january, 2, 4));
        january.Sum(v => v.Value).ShouldBeGreaterThan(july.Sum(v => v.Value));
    }

    [Fact]
    public void Commercial_load_stays_flat_around_the_average()
    {
        var values = new SyntheticProfileGenerator(NoDefects)
            .GenerateCommercial(SeriesId, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 7), averageLoadKw: 40m)
            .ToList();

        // 40 kW over a quarter hour is 10 kWh; noise is ±5 %.
        values.ShouldAllBe(v => v.Value >= 9.5m && v.Value <= 10.5m);
    }

    [Fact]
    public void Photovoltaic_produces_nothing_at_night_and_more_in_summer_than_in_winter()
    {
        var june = Generate(Profile.Photovoltaic, new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 21), NoDefects);
        var december = Generate(Profile.Photovoltaic, new DateOnly(2026, 12, 14), new DateOnly(2026, 12, 20), NoDefects);

        june.Where(v => LocalHour(v) is >= 23 or < 4).ShouldAllBe(v => v.Value == 0m);
        june.Where(v => LocalHour(v) is 13).ShouldAllBe(v => v.Value > 0m);
        june.ShouldAllBe(v => v.Value >= 0m);
        june.Sum(v => v.Value).ShouldBeGreaterThan(2 * december.Sum(v => v.Value));
    }

    [Fact]
    public void Photovoltaic_does_not_shift_with_the_clock_change()
    {
        // Solar noon is ~11:20 UTC all year; in local time it moves from ~12:20 (CET) to ~13:20 (CEST).
        var values = Generate(Profile.Photovoltaic, new DateOnly(2026, 3, 27), new DateOnly(2026, 3, 31), NoDefects);

        foreach (var day in values.Where(v => v.Value > 0).GroupBy(v => v.IntervalStart.UtcDateTime.Date))
        {
            var noonUtc = day.Average(v => v.IntervalStart.TimeOfDay.TotalHours + 0.125);
            noonUtc.ShouldBeInRange(10.8, 11.8);
        }
    }

    [Theory]
    [InlineData(-0.01, 0)]
    [InlineData(1.01, 0)]
    [InlineData(0, -0.01)]
    [InlineData(0, double.NaN)]
    public void Probabilities_outside_zero_to_one_are_rejected(double gap, double spike) =>
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new SyntheticProfileGenerator(new SyntheticProfileOptions { GapProbability = gap, SpikeProbability = spike }));

    [Fact]
    public void Range_ending_before_it_starts_is_rejected_eagerly() =>
        Should.Throw<ArgumentException>(() =>
            new SyntheticProfileGenerator().GenerateHousehold(SeriesId, new DateOnly(2026, 5, 2), new DateOnly(2026, 5, 1)));

    [Fact]
    public void Non_positive_scale_is_rejected() =>
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new SyntheticProfileGenerator().GeneratePhotovoltaic(SeriesId, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 1), 0m));

    private static List<MeasurementValue> Generate(Profile profile, DateOnly first, DateOnly last, SyntheticProfileOptions options)
    {
        var generator = new SyntheticProfileGenerator(options);
        var values = profile switch
        {
            Profile.Household => generator.GenerateHousehold(SeriesId, first, last),
            Profile.Commercial => generator.GenerateCommercial(SeriesId, first, last),
            Profile.Photovoltaic => generator.GeneratePhotovoltaic(SeriesId, first, last),
            _ => throw new ArgumentOutOfRangeException(nameof(profile)),
        };
        return values.ToList();
    }

    private static DateTimeOffset LocalMidnight(DateOnly day)
    {
        var midnight = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(midnight, Berlin.GetUtcOffset(midnight));
    }

    private static int LocalHour(MeasurementValue value) => TimeZoneInfo.ConvertTime(value.IntervalStart, Berlin).Hour;

    private static decimal MeanAtLocalHours(IEnumerable<MeasurementValue> values, int fromHour, int toHourExclusive) =>
        values.Where(v => LocalHour(v) >= fromHour && LocalHour(v) < toHourExclusive).Average(v => v.Value);
}
