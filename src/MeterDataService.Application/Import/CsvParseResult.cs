namespace MeterDataService.Application.Import;

/// <summary>A row that could not be read at all, e.g. an unparseable timestamp.</summary>
public sealed record CsvParseError(int LineNumber, string Message);

public sealed record CsvParseResult(IReadOnlyList<ImportedReading> Readings, IReadOnlyList<CsvParseError> Errors);
