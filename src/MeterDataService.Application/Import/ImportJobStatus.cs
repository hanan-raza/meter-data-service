namespace MeterDataService.Application.Import;

public enum ImportJobState
{
    Queued = 1,
    Processing = 2,
    Completed = 3,

    /// <summary>Processing threw; the file itself may be fine. Rejected rows don't make a job fail.</summary>
    Failed = 4,
}

/// <summary>Counts from parsing and validating one file.</summary>
/// <param name="RowsRead">Data rows that could be parsed.</param>
/// <param name="ParseErrors">Rows that could not be read at all (see <see cref="CsvImportParser"/>).</param>
/// <param name="Accepted">Parsed values that passed every plausibility rule.</param>
/// <param name="Rejected">Parsed values rejected by a plausibility rule; candidates for gap filling.</param>
/// <param name="MissingIntervals">Intervals in the covered days without any value.</param>
/// <param name="Findings">Period-level warnings, e.g. one per contiguous gap or wrong DST interval count.</param>
public sealed record ImportSummary(int RowsRead, int ParseErrors, int Accepted, int Rejected, int MissingIntervals, int Findings);

/// <summary>Where a job stands; what the client sees when polling.</summary>
public sealed record ImportJobStatus(
    Guid JobId,
    string MarketLocationId,
    string FileName,
    ImportJobState State,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? FinishedAt = null,
    ImportSummary? Summary = null,
    string? Error = null);
