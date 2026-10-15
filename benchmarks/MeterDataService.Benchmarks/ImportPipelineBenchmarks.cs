using System.Threading.Channels;
using BenchmarkDotNet.Attributes;
using MeterDataService.Application.GapFilling;
using MeterDataService.Application.Import;
using MeterDataService.Domain;
using MeterDataService.Infrastructure.AnomalyDetection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeterDataService.Benchmarks;

/// <summary>
/// Imports one week of 15-minute values (672 intervals) for one market location. The single steps are measured
/// on their own to locate a hotspot; <see cref="Pipeline"/> is the core import as the roadmap defines it, and
/// <see cref="EndToEnd"/> adds what the real background service does on top: ML.NET anomaly detection, the
/// quality score, job tracking and metrics.
/// </summary>
[MemoryDiagnoser]
public class ImportPipelineBenchmarks
{
    private ImportScenario _scenario = null!;
    private CsvParseResult _parsed = null!;
    private ValidationReport _report = null!;
    private readonly ValidationEngine _validationEngine = new();
    private readonly GapFillingService _gapFillingService = new(NullLogger<GapFillingService>.Instance);
    private readonly AnomalyDetector _anomalyDetector = new(NullLogger<AnomalyDetector>.Instance);

    [GlobalSetup]
    public void Setup()
    {
        _scenario = ImportScenario.Create();
        using (var reader = new StringReader(_scenario.Csv))
        {
            _parsed = CsvImportParser.Parse(reader);
        }

        _report = _validationEngine.Validate(_parsed.Readings);
        VerifyScenario();
    }

    [Benchmark(Description = "1 CSV parse")]
    public CsvParseResult ParseCsv()
    {
        using var reader = new StringReader(_scenario.Csv);
        return CsvImportParser.Parse(reader);
    }

    [Benchmark(Description = "2 Validate")]
    public ValidationReport Validate() => _validationEngine.Validate(_parsed.Readings);

    /// <summary>Loading the history into a fresh series is included: every import starts from a newly loaded one.</summary>
    [Benchmark(Description = "3 Record + gap fill")]
    public GapFillResult RecordAndFillGaps()
    {
        var series = _scenario.NewSeriesWithHistory();
        RecordAccepted(series, _report);
        return _gapFillingService.Fill(series, ImportScenario.FirstDay, ImportScenario.LastDay);
    }

    [Benchmark(Baseline = true, Description = "Pipeline: parse → validate → gap fill → repository")]
    public async Task<int> Pipeline()
    {
        using var reader = new StringReader(_scenario.Csv);
        var parsed = CsvImportParser.Parse(reader);
        var report = _validationEngine.Validate(parsed.Readings);

        var repository = new InMemoryMeasurementSeriesRepository(_scenario);
        var series = await repository.FindEnergySeriesAsync(
            ImportScenario.MaLoId,
            GermanCalendar.StartOfDayUtc(ImportScenario.FirstDay.AddDays(-SimilarDayFiller.LookbackDays)),
            GermanCalendar.StartOfDayUtc(ImportScenario.LastDay.AddDays(1)) + MeasurementSeries.IntervalLength,
            CancellationToken.None) ?? throw new InvalidOperationException("Scenario market location is unknown.");

        RecordAccepted(series, report);
        var filled = _gapFillingService.Fill(series, ImportScenario.FirstDay, ImportScenario.LastDay);
        await repository.SaveChangesAsync(CancellationToken.None);
        return filled.Replaced.Count;
    }

    [Benchmark(Description = "End to end: ImportBackgroundService incl. ML.NET")]
    public async Task<ImportSummary> EndToEnd()
    {
        var tracker = new ImportJobTracker(TimeProvider.System);
        var repository = new InMemoryMeasurementSeriesRepository(_scenario);
        using var meterFactory = new NoOpMeterFactory();
        await using var services = new ServiceCollection()
            .AddScoped<IMeasurementSeriesRepository>(_ => repository)
            .BuildServiceProvider();

        var queue = Channel.CreateUnbounded<ImportJob>();
        var job = new ImportJob(Guid.NewGuid(), ImportScenario.MaLoId, "week.csv", _scenario.Csv, DateTimeOffset.UtcNow);
        tracker.MarkQueued(job);
        queue.Writer.TryWrite(job);
        queue.Writer.Complete();

        var channel = new ImportChannel(queue);
        using var service = new ImportBackgroundService(
            channel,
            tracker,
            _validationEngine,
            _gapFillingService,
            _anomalyDetector,
            services.GetRequiredService<IServiceScopeFactory>(),
            new ImportMetrics(meterFactory, channel),
            NullLogger<ImportBackgroundService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!;

        return tracker.Find(job.Id)?.Summary
            ?? throw new InvalidOperationException($"Import failed: {tracker.Find(job.Id)?.Error}");
    }

    private static void RecordAccepted(MeasurementSeries series, ValidationReport report)
    {
        foreach (var accepted in report.Values.Where(v => v.Result.Outcome == ValidationOutcome.Ok))
        {
            series.Record(accepted.Reading.IntervalStart, accepted.Reading.Value, accepted.Reading.Status);
        }
    }

    // A benchmark of a pipeline that silently does nothing would be fast and worthless.
    private void VerifyScenario()
    {
        var summary = EndToEnd().GetAwaiter().GetResult();
        var expectedSubstitutes = ImportScenario.MissingIntervals + 1;
        if (summary.RowsRead != _scenario.Rows
            || summary.ParseErrors != 0
            || summary.Rejected != 1
            || summary.MissingIntervals != ImportScenario.MissingIntervals
            || summary.Substituted != expectedSubstitutes
            || Pipeline().GetAwaiter().GetResult() != expectedSubstitutes)
        {
            throw new InvalidOperationException($"Scenario did not import as expected: {summary}.");
        }
    }
}
