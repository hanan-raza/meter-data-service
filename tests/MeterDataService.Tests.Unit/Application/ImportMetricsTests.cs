using MeterDataService.Application.Import;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Shouldly;

namespace MeterDataService.Tests.Unit.Application;

public sealed class ImportMetricsTests : IDisposable
{
    private readonly FakeMeterFactory _meterFactory = new();

    public void Dispose() => _meterFactory.Dispose();

    [Fact]
    public void Queue_depth_gauge_reports_the_jobs_waiting_in_the_channel()
    {
        var channel = new ImportChannel(capacity: 5);
        _ = new ImportMetrics(_meterFactory, channel);
        using var depth = new MetricCollector<int>(_meterFactory, ImportMetrics.MeterName, ImportMetrics.QueueDepth);
        channel.TryEnqueue(ImportChannelTests.Job());
        channel.TryEnqueue(ImportChannelTests.Job());

        depth.RecordObservableInstruments();

        depth.LastMeasurement.ShouldNotBeNull().Value.ShouldBe(2);
    }

    [Fact]
    public void Values_are_counted_per_validation_outcome()
    {
        var metrics = new ImportMetrics(_meterFactory, new ImportChannel());
        using var values = new MetricCollector<long>(_meterFactory, ImportMetrics.MeterName, ImportMetrics.ValuesImported);

        metrics.RecordValues(new ImportSummary(RowsRead: 96, ParseErrors: 0, Accepted: 94, Rejected: 2, MissingIntervals: 0, Findings: 2, Substituted: 2));

        values.GetMeasurementSnapshot().Select(m => (Outcome: m.Tags[ImportMetrics.OutcomeTag] as string, m.Value))
            .ShouldBe([("accepted", 94L), ("rejected", 2L)], ignoreOrder: true);
    }
}
