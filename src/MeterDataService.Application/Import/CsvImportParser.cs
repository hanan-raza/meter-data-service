using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using MeterDataService.Domain;

namespace MeterDataService.Application.Import;

/// <summary>
/// Reads interval values from CSV with the columns <c>timestamp</c>, <c>value</c> and <c>status</c>
/// (comma or semicolon separated, decimal point or decimal comma).
/// </summary>
/// <remarks>
/// <para>
/// Timestamps are the interval start in ISO 8601. With an offset (<c>2026-10-25T02:00:00+02:00</c>) they are
/// unambiguous. Without one they are German local time, as many exports deliver them: the repeated 02:00 hour
/// on the fall-back day is then resolved by order of appearance (first summer time, then winter time), and
/// local times skipped on the spring-forward day are errors.
/// </para>
/// <para>
/// Rows are parsed independently: a malformed row becomes a <see cref="CsvParseError"/> with its line number,
/// and the rest of the file is still read. Plausibility (negative values, spikes, gaps) is not checked here;
/// that is the job of <see cref="ValidationEngine"/>.
/// </para>
/// </remarks>
public static class CsvImportParser
{
    public const string TimestampColumn = "timestamp";
    public const string ValueColumn = "value";
    public const string StatusColumn = "status";

    private static readonly string[] OffsetFormats =
        ["yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mmzzz", "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm'Z'"];

    private static readonly string[] LocalFormats =
        ["yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm"];

    // numeric(18,5) leaves 13 integer digits.
    private const decimal MaxAbsoluteValue = 9_999_999_999_999.99999m;

    public static CsvParseResult Parse(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            DetectDelimiter = true,
            PrepareHeaderForMatch = args => args.Header.Trim().ToLowerInvariant(),
            TrimOptions = TrimOptions.Trim,
            MissingFieldFound = null,
            BadDataFound = null,
        };

        using var csv = new CsvReader(reader, config, leaveOpen: true);
        var readings = new List<ImportedReading>();
        var errors = new List<CsvParseError>();

        if (!csv.Read() || !csv.ReadHeader())
        {
            errors.Add(new CsvParseError(1, $"File is empty; expected a header row '{TimestampColumn},{ValueColumn},{StatusColumn}'."));
            return new CsvParseResult(readings, errors);
        }

        var missingColumns = new[] { TimestampColumn, ValueColumn, StatusColumn }
            .Where(column => csv.GetFieldIndex(column, isTryGet: true) < 0)
            .ToList();
        if (missingColumns.Count > 0)
        {
            errors.Add(new CsvParseError(1, $"Header is missing column(s): {string.Join(", ", missingColumns)}."));
            return new CsvParseResult(readings, errors);
        }

        var seenAmbiguousTimes = new HashSet<DateTime>();
        while (csv.Read())
        {
            var line = csv.Parser.Row;
            var error = TryParseRow(
                csv.GetField(TimestampColumn), csv.GetField(ValueColumn), csv.GetField(StatusColumn), seenAmbiguousTimes,
                out var intervalStart, out var value, out var status);

            if (error is null)
            {
                readings.Add(new ImportedReading(line, intervalStart, value, status));
            }
            else
            {
                errors.Add(new CsvParseError(line, error));
            }
        }

        return new CsvParseResult(readings, errors);
    }

    private static string? TryParseRow(
        string? rawTimestamp, string? rawValue, string? rawStatus, HashSet<DateTime> seenAmbiguousTimes,
        out DateTimeOffset intervalStart, out decimal value, out MeasurementStatus status)
    {
        value = default;
        status = default;

        var error = TryParseTimestamp(rawTimestamp, seenAmbiguousTimes, out intervalStart)
            ?? TryParseValue(rawValue, out value);
        if (error is not null)
        {
            return error;
        }

        if (!TryParseStatus(rawStatus, out status))
        {
            return $"Status '{rawStatus}' is unknown; expected Measured, Estimated or Replaced.";
        }

        return null;
    }

    private static string? TryParseTimestamp(string? raw, HashSet<DateTime> seenAmbiguousTimes, out DateTimeOffset utc)
    {
        utc = default;
        if (string.IsNullOrEmpty(raw))
        {
            return "Timestamp is missing.";
        }

        if (DateTimeOffset.TryParseExact(raw, OffsetFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var withOffset))
        {
            utc = withOffset.ToUniversalTime();
        }
        else if (DateTime.TryParseExact(raw, LocalFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            var zone = GermanCalendar.TimeZone;
            if (zone.IsInvalidTime(local))
            {
                return $"Local time '{raw}' does not exist: clocks jump from 02:00 to 03:00 on that day.";
            }

            var offset = zone.GetUtcOffset(local);
            if (zone.IsAmbiguousTime(local))
            {
                var offsets = zone.GetAmbiguousTimeOffsets(local);
                offset = seenAmbiguousTimes.Add(local) ? offsets.Max() : offsets.Min();
            }

            utc = new DateTimeOffset(local, offset).ToUniversalTime();
        }
        else
        {
            return $"Timestamp '{raw}' is not an ISO 8601 date and time.";
        }

        if (utc.Ticks % MeasurementSeries.IntervalLength.Ticks != 0)
        {
            return $"Timestamp '{raw}' is not on a quarter-hour boundary.";
        }

        return null;
    }

    private static string? TryParseValue(string? raw, out decimal value)
    {
        value = default;
        if (string.IsNullOrEmpty(raw))
        {
            return "Value is missing.";
        }

        // Semicolon-separated German exports use a decimal comma.
        var normalized = raw.Contains('.', StringComparison.Ordinal) ? raw : raw.Replace(',', '.');
        if (!decimal.TryParse(normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value))
        {
            return $"Value '{raw}' is not a decimal number.";
        }

        if (decimal.Round(value, MeasurementValue.Scale) != value)
        {
            return $"Value '{raw}' has more than {MeasurementValue.Scale} decimal places.";
        }

        if (Math.Abs(value) > MaxAbsoluteValue)
        {
            return $"Value '{raw}' exceeds the storable range.";
        }

        return null;
    }

    private static bool TryParseStatus(string? raw, out MeasurementStatus status)
    {
        // Enum.TryParse would also accept numbers like "7", which no sender means as a status.
        status = raw?.ToUpperInvariant() switch
        {
            "MEASURED" => MeasurementStatus.Measured,
            "ESTIMATED" => MeasurementStatus.Estimated,
            "REPLACED" => MeasurementStatus.Replaced,
            _ => default,
        };
        return status != default;
    }
}
