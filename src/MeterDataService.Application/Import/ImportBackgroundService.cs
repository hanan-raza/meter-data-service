using MeterDataService.Application.GapFilling;
using MeterDataService.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MeterDataService.Application.Import;

/// <summary>
/// Takes queued CSV files off the <see cref="ImportChannel"/> one at a time, parses them, runs the plausibility
/// checks (Plausibilisierung) and gap filling (Ersatzwertbildung), persists the result and records the outcome
/// in the <see cref="ImportJobTracker"/>.
/// </summary>
/// <remarks>
/// Uploads only enqueue, so a slow or large import never holds an HTTP request open. Jobs are processed
/// sequentially, so two files for the same market location never race on the same intervals.
/// </remarks>
public sealed partial class ImportBackgroundService(
    ImportChannel channel,
    ImportJobTracker tracker,
    ValidationEngine validationEngine,
    GapFillingService gapFillingService,
    IServiceScopeFactory scopeFactory,
    ILogger<ImportBackgroundService> logger) : BackgroundService
{
    public const string FailureMessage = "Import failed unexpectedly. The server log has the details for this job id.";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var job in channel.Reader.ReadAllAsync(stoppingToken))
            {
                await HandleAsync(job, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown; jobs still queued are lost with the in-memory channel.
        }
    }

    public static string UnknownMarketLocationMessage(string maLoId) =>
        $"Market location {maLoId} is unknown or has no energy series. Its master data must exist before values can be imported.";

    private async Task HandleAsync(ImportJob job, CancellationToken cancellationToken)
    {
        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["ImportJobId"] = job.Id,
            ["MarketLocationId"] = job.MarketLocationId,
        });

        try
        {
            tracker.MarkProcessing(job.Id);
            var (summary, error) = await ImportAsync(job, cancellationToken);
            if (summary is null)
            {
                LogImportRefused(job.FileName, error);
                tracker.MarkFailed(job.Id, error);
                return;
            }

            tracker.MarkCompleted(job.Id, summary);
            LogImportCompleted(job.FileName, summary.RowsRead, summary.ParseErrors, summary.Accepted, summary.Rejected, summary.MissingIntervals, summary.Substituted);
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

    private async Task<(ImportSummary? Summary, string Error)> ImportAsync(ImportJob job, CancellationToken cancellationToken)
    {
        using var reader = new StringReader(job.Content);
        var parsed = CsvImportParser.Parse(reader);
        var report = validationEngine.Validate(parsed.Readings);

        var substituted = 0;
        if (parsed.Readings.Count > 0)
        {
            var firstDay = GermanCalendar.DayOf(parsed.Readings.Min(r => r.IntervalStart));
            var lastDay = GermanCalendar.DayOf(parsed.Readings.Max(r => r.IntervalStart));

            // A scope per job: the DbContext behind the repository is scoped, this service is a singleton.
            await using var scope = scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IMeasurementSeriesRepository>();

            // Similar-day filling needs the lookback days, interpolation the first value after the period as its anchor.
            var series = await repository.FindEnergySeriesAsync(
                job.MarketLocationId,
                GermanCalendar.StartOfDayUtc(firstDay.AddDays(-SimilarDayFiller.LookbackDays)),
                GermanCalendar.StartOfDayUtc(lastDay.AddDays(1)) + MeasurementSeries.IntervalLength,
                cancellationToken);
            if (series is null)
            {
                return (null, UnknownMarketLocationMessage(job.MarketLocationId));
            }

            // Rejected readings are not stored at all, so a value stored by an earlier delivery stays untouched
            // and an interval without one is simply missing. Gap filling therefore needs no rejected set.
            foreach (var accepted in report.Values.Where(v => v.Result.Outcome == ValidationOutcome.Ok))
            {
                series.Record(accepted.Reading.IntervalStart, accepted.Reading.Value, accepted.Reading.Status);
            }

            substituted = gapFillingService.Fill(series, firstDay, lastDay).Replaced.Count;
            await repository.SaveChangesAsync(cancellationToken);
        }

        var summary = new ImportSummary(
            RowsRead: parsed.Readings.Count,
            ParseErrors: parsed.Errors.Count,
            Accepted: report.Values.Count(v => v.Result.Outcome == ValidationOutcome.Ok),
            Rejected: report.Values.Count(v => v.Result.Outcome == ValidationOutcome.Rejected),
            MissingIntervals: report.MissingIntervals.Count,
            Findings: report.Findings.Count,
            Substituted: substituted);
        return (summary, string.Empty);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Imported {FileName}: {RowsRead} rows read, {ParseErrors} parse errors, {Accepted} accepted, {Rejected} rejected, {MissingIntervals} missing intervals, {Substituted} substituted")]
    private partial void LogImportCompleted(string fileName, int rowsRead, int parseErrors, int accepted, int rejected, int missingIntervals, int substituted);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Import of {FileName} refused: {Reason}")]
    private partial void LogImportRefused(string fileName, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Import of {FileName} failed")]
    private partial void LogImportFailed(Exception exception, string fileName);
}
