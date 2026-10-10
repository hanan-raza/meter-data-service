using MeterDataService.Domain;
using Shouldly;

namespace MeterDataService.Tests.Unit.Domain;

public class GermanCalendarTests
{
    [Theory]
    [InlineData(2026, 6, 10, 96)]
    [InlineData(2026, 3, 29, 92)]
    [InlineData(2026, 10, 25, 100)]
    public void Day_length_follows_clock_changes(int year, int month, int day, int expected)
    {
        GermanCalendar.IntervalsIn(new DateOnly(year, month, day)).ShouldBe(expected);
    }

    [Fact]
    public void Start_of_day_is_local_midnight_in_utc()
    {
        GermanCalendar.StartOfDayUtc(new DateOnly(2026, 1, 15)).ShouldBe(new DateTimeOffset(2026, 1, 14, 23, 0, 0, TimeSpan.Zero));
        GermanCalendar.StartOfDayUtc(new DateOnly(2026, 7, 15)).ShouldBe(new DateTimeOffset(2026, 7, 14, 22, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Day_of_uses_local_date_not_utc_date()
    {
        GermanCalendar.DayOf(new DateTimeOffset(2026, 7, 14, 22, 0, 0, TimeSpan.Zero)).ShouldBe(new DateOnly(2026, 7, 15));
        GermanCalendar.DayOf(new DateTimeOffset(2026, 7, 14, 21, 45, 0, TimeSpan.Zero)).ShouldBe(new DateOnly(2026, 7, 14));
    }

    [Fact]
    public void German_time_shows_both_passes_of_the_repeated_hour_with_their_own_offset()
    {
        var firstPass = GermanCalendar.ToGermanTime(new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero));
        var secondPass = GermanCalendar.ToGermanTime(new DateTimeOffset(2026, 10, 25, 1, 0, 0, TimeSpan.Zero));

        firstPass.DateTime.ShouldBe(new DateTime(2026, 10, 25, 2, 0, 0));
        firstPass.Offset.ShouldBe(TimeSpan.FromHours(2));
        secondPass.DateTime.ShouldBe(new DateTime(2026, 10, 25, 2, 0, 0));
        secondPass.Offset.ShouldBe(TimeSpan.FromHours(1));
    }
}
