using System.Diagnostics.Metrics;

namespace MeterDataService.Application.Import;

/// <summary>
/// Throughput instruments of the import pipeline: values imported, time per import job and jobs waiting in the
/// <see cref="ImportChannel"/>.
/// </summary>
/// <remarks>
/// The meter comes from <see cref="IMeterFactory"/> instead of a static <see cref="Meter"/>, so each host (and each
/// test) gets its own instruments and a listener only sees the measurements of the host it listens to.
/// </remarks>
public sealed class ImportMetrics
{
    public const string MeterName = "MeterDataService.Import";
    public const string ValuesImported = "meterdata.import.values";
    public const string JobDuration = "meterdata.import.duration";
    public const string QueueDepth = "meterdata.import.queue.depth";

    /// <summary>Tag on <see cref="ValuesImported"/>: <c>accepted</c> or <c>rejected</c> by the plausibility checks.</summary>
    public const string OutcomeTag = "meterdata.validation.outcome";

    /// <summary>Tag on <see cref="JobDuration"/>: <c>completed</c>, <c>refused</c> or <c>failed</c>.</summary>
    public const string ResultTag = "meterdata.import.result";

    private readonly Counter<long> _values;
    private readonly Histogram<double> _duration;

    public ImportMetrics(IMeterFactory meterFactory, ImportChannel channel)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        ArgumentNullException.ThrowIfNull(channel);

        var meter = meterFactory.Create(MeterName);
        _values = meter.CreateCounter<long>(
            ValuesImported, unit: "{value}", description: "Readings read from imported files, by validation outcome.");
        _duration = meter.CreateHistogram<double>(
            JobDuration, unit: "s", description: "Time to process one import job, from dequeue to persisted result.");

        // Observed at export time, so the channel needs no callback when a job is queued or taken.
        meter.CreateObservableGauge(
            QueueDepth, () => channel.Count, unit: "{job}", description: "Import jobs waiting in the queue.");
    }

    public void RecordValues(ImportSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        _values.Add(summary.Accepted, new KeyValuePair<string, object?>(OutcomeTag, "accepted"));
        _values.Add(summary.Rejected, new KeyValuePair<string, object?>(OutcomeTag, "rejected"));
    }

    public void RecordJob(TimeSpan duration, string result) =>
        _duration.Record(duration.TotalSeconds, new KeyValuePair<string, object?>(ResultTag, result));
}
