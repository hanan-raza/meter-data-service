using MeterDataService.Application.Import;
using MeterDataService.Domain;
using Shouldly;

namespace MeterDataService.Tests.Unit.Application;

public class CsvImportParserTests
{
    [Fact]
    public void Parses_rows_with_offset_timestamps_into_utc_readings()
    {
        var result = Parse(
            "timestamp,value,status",
            "2026-07-01T12:00:00+02:00,1.25,Measured",
            "2026-07-01T10:15:00Z,0.5,estimated",
            "2026-07-01T12:30+02:00,-0.12345,REPLACED");

        result.Errors.ShouldBeEmpty();
        result.Readings.ShouldBe(
        [
            new ImportedReading(2, Utc(2026, 7, 1, 10, 0), 1.25m, MeasurementStatus.Measured),
            new ImportedReading(3, Utc(2026, 7, 1, 10, 15), 0.5m, MeasurementStatus.Estimated),
            new ImportedReading(4, Utc(2026, 7, 1, 10, 30), -0.12345m, MeasurementStatus.Replaced),
        ]);
        result.Readings.ShouldAllBe(r => r.IntervalStart.Offset == TimeSpan.Zero);
    }

    [Fact]
    public void Accepts_semicolon_separator_with_decimal_comma_and_any_column_order()
    {
        var result = Parse(
            "Status; Value; Timestamp",
            "Measured;1,5;2026-01-15T00:00:00+01:00",
            "Measured;2.75;2026-01-15T00:15:00+01:00");

        result.Errors.ShouldBeEmpty();
        result.Readings.Select(r => r.Value).ShouldBe([1.5m, 2.75m]);
        result.Readings[0].IntervalStart.ShouldBe(Utc(2026, 1, 14, 23, 0));
    }

    [Fact]
    public void Readme_example_parses()
    {
        var result = Parse(
            "timestamp;value;status",
            "2026-10-25T02:00:00+02:00;0,412;Measured",
            "2026-10-25T02:00:00+01:00;0,398;Measured",
            "2026-10-25 03:00;0.405;Estimated");

        result.Errors.ShouldBeEmpty();
        result.Readings.Select(r => r.IntervalStart).ShouldBe([Utc(2026, 10, 25, 0, 0), Utc(2026, 10, 25, 1, 0), Utc(2026, 10, 25, 2, 0)]);
        result.Readings.Select(r => r.Value).ShouldBe([0.412m, 0.398m, 0.405m]);
    }

    [Fact]
    public void Local_timestamps_on_fall_back_day_resolve_repeated_hour_by_order()
    {
        var day = new DateOnly(2026, 10, 25);
        var midnight = day.ToDateTime(TimeOnly.MinValue);
        var quarterHours = Enumerable.Range(0, 96).Select(q => midnight.AddMinutes(15 * q)).ToList();
        // The clock shows 02:00–02:45 twice: once before and once after the switch back from 03:00.
        quarterHours.InsertRange(12, quarterHours.Where(t => t.Hour == 2).ToList());
        var rows = quarterHours.Select(t => $"{t:yyyy-MM-dd HH:mm},1,Measured");

        var result = Parse(["timestamp,value,status", .. rows]);

        result.Errors.ShouldBeEmpty();
        result.Readings.Count.ShouldBe(100);
        result.Readings.Zip(result.Readings.Skip(1))
            .ShouldAllBe(pair => pair.Second.IntervalStart - pair.First.IntervalStart == MeasurementSeries.IntervalLength);
        result.Readings[0].IntervalStart.ShouldBe(GermanCalendar.StartOfDayUtc(day));
        // First 02:00 is summer time (00:00 UTC), the repeat is winter time (01:00 UTC).
        result.Readings.Where(r => r.LineNumber is 10 or 14).Select(r => r.IntervalStart)
            .ShouldBe([Utc(2026, 10, 25, 0, 0), Utc(2026, 10, 25, 1, 0)]);
    }

    [Fact]
    public void Local_time_skipped_by_spring_forward_is_an_error()
    {
        var result = Parse(
            "timestamp,value,status",
            "2026-03-29T01:45,1,Measured",
            "2026-03-29T02:00,1,Measured",
            "2026-03-29T03:00,1,Measured");

        result.Readings.Select(r => r.IntervalStart).ShouldBe([Utc(2026, 3, 29, 0, 45), Utc(2026, 3, 29, 1, 0)]);
        result.Errors.ShouldHaveSingleItem().LineNumber.ShouldBe(3);
        result.Errors[0].Message.ShouldContain("does not exist");
    }

    [Theory]
    [InlineData("2026/07/01 12:00,1,Measured", "not an ISO 8601")]
    [InlineData("01.07.2026 12:00,1,Measured", "not an ISO 8601")]
    [InlineData("2026-07-01T12:07:00+02:00,1,Measured", "quarter-hour")]
    [InlineData("2026-07-01T12:00:00+02:00,abc,Measured", "not a decimal")]
    [InlineData("2026-07-01T12:00:00+02:00,1.123456,Measured", "decimal places")]
    [InlineData("2026-07-01T12:00:00+02:00,99999999999999,Measured", "storable range")]
    [InlineData("2026-07-01T12:00:00+02:00,1,Guessed", "unknown")]
    [InlineData("2026-07-01T12:00:00+02:00,1,3", "unknown")]
    [InlineData("2026-07-01T12:00:00+02:00,,Measured", "missing")]
    [InlineData("2026-07-01T12:00:00+02:00,1", "unknown")]
    public void Malformed_row_becomes_error_with_line_number_and_rest_is_kept(string badRow, string expectedMessage)
    {
        var result = Parse(
            "timestamp,value,status",
            "2026-07-01T11:45:00+02:00,1,Measured",
            badRow,
            "2026-07-01T12:15:00+02:00,1,Measured");

        var error = result.Errors.ShouldHaveSingleItem();
        error.LineNumber.ShouldBe(3);
        error.Message.ShouldContain(expectedMessage);
        result.Readings.Select(r => r.LineNumber).ShouldBe([2, 4]);
    }

    [Fact]
    public void Missing_column_in_header_is_reported()
    {
        var result = Parse("timestamp,value", "2026-07-01T12:00:00+02:00,1");

        result.Readings.ShouldBeEmpty();
        result.Errors.ShouldHaveSingleItem().Message.ShouldContain("status");
    }

    [Fact]
    public void Empty_file_is_reported()
    {
        var result = CsvImportParser.Parse(new StringReader(string.Empty));

        result.Readings.ShouldBeEmpty();
        result.Errors.ShouldHaveSingleItem().LineNumber.ShouldBe(1);
    }

    private static CsvParseResult Parse(params string[] lines) =>
        CsvImportParser.Parse(new StringReader(string.Join("\n", lines)));

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);
}
