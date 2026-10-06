using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MeterDataService.Application.Import;

/// <summary>
/// Takes queued CSV files off the <see cref="ImportChannel"/> one at a time, parses them and runs the
/// plausibility checks (Plausibilisierung), and records the outcome in the <see cref="ImportJobTracker"/>.
/// </summary>
/// <remarks>
/// Uploads only enqueue, so a slow or large import never holds an HTTP request open. Jobs are processed
/// sequentially: once values are persisted, two files for the same market location must not race.
/// </remarks>
public sealed partial class ImportBackgroundService(
    ImportChannel channel,
    ImportJobTracker tracker,
    ValidationEngine validationEngine,
    ILogger<ImportBackgroundService> logger) : BackgroundService
{
    public const string FailureMessage = "Import failed unexpectedly. The server log has the details for this job id.";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var job in channel.Reader.ReadAllAsync(stoppingToken))
            {
                Handle(job);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown; jobs still queued are lost with the in-memory channel.
        }
    }

    private void Handle(ImportJob job)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["ImportJobId"] = job.Id,
            ["MarketLocationId"] = job.MarketLocationId,
        });

        try
        {
            tracker.MarkProcessing(job.Id);
            var summary = Import(job);
            tracker.MarkCompleted(job.Id, summary);
            LogImportCompleted(job.FileName, summary.RowsRead, summary.ParseErrors, summary.Accepted, summary.Rejected, summary.MissingIntervals);
        }
#pragma warning disable CA1031 // One broken job must not stop the loop, or every job behind it waits forever.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogImportFailed(ex, job.FileName);
            if (tracker.Find(job.Id) is not null)
            {
                // The exception message stays in the log; clients only get the job id to ask about.
                tracker.MarkFailed(job.Id, FailureMessage);
            }
        }
    }

    private ImportSummary Import(ImportJob job)
    {
        using var reader = new StringReader(job.Content);
        var parsed = CsvImportParser.Parse(reader);
        var report = validationEngine.Validate(parsed.Readings);

        return new ImportSummary(
            RowsRead: parsed.Readings.Count,
            ParseErrors: parsed.Errors.Count,
            Accepted: report.Values.Count(v => v.Result.Outcome == ValidationOutcome.Ok),
            Rejected: report.Values.Count(v => v.Result.Outcome == ValidationOutcome.Rejected),
            MissingIntervals: report.MissingIntervals.Count,
            Findings: report.Findings.Count);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Imported {FileName}: {RowsRead} rows read, {ParseErrors} parse errors, {Accepted} accepted, {Rejected} rejected, {MissingIntervals} missing intervals")]
    private partial void LogImportCompleted(string fileName, int rowsRead, int parseErrors, int accepted, int rejected, int missingIntervals);

    [LoggerMessage(Level = LogLevel.Error, Message = "Import of {FileName} failed")]
    private partial void LogImportFailed(Exception exception, string fileName);
}
