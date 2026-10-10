using MeterDataService.Application.Aggregation;
using Shouldly;

namespace MeterDataService.Tests.Unit.Application;

public class ReportingPeriodTests
{
    private static readonly DateOnly Day = new(2026, 6, 10);

    [Fact]
    public void A_single_day_is_valid_at_every_granularity()
    {
        foreach (var granularity in Enum.GetValues<AggregationGranularity>())
        {
            ReportingPeriod.Validate(Day, Day, granularity).ShouldBeNull();
        }
    }

    [Fact]
    public void To_before_from_is_invalid()
    {
        ReportingPeriod.Validate(Day, Day.AddDays(-1), AggregationGranularity.Day).ShouldNotBeNull().ShouldContain("before");
    }

    [Theory]
    [InlineData(AggregationGranularity.QuarterHour, 31)]
    [InlineData(AggregationGranularity.Hour, 92)]
    [InlineData(AggregationGranularity.Day, 1098)]
    [InlineData(AggregationGranularity.Month, 1098)]
    public void The_longest_period_is_allowed_and_one_day_more_is_not(AggregationGranularity granularity, int maxDays)
    {
        ReportingPeriod.Validate(Day, Day.AddDays(maxDays - 1), granularity).ShouldBeNull();
        ReportingPeriod.Validate(Day, Day.AddDays(maxDays), granularity).ShouldNotBeNull().ShouldContain($"at most {maxDays} days");
    }

    [Fact]
    public void A_full_october_with_its_25_hour_day_fits_the_quarter_hour_limit()
    {
        // 31 days, 2,980 intervals: the fall-back day doesn't count as a 32nd day.
        ReportingPeriod.Validate(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), AggregationGranularity.QuarterHour).ShouldBeNull();
    }
}
