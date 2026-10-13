using System.Diagnostics.Metrics;

namespace MeterDataService.Tests.Unit.Application;

/// <summary>
/// Creates meters scoped to this factory, so a <c>MetricCollector</c> bound to it only sees the measurements of
/// the test that owns it, even when tests run in parallel.
/// </summary>
internal sealed class FakeMeterFactory : IMeterFactory
{
    private readonly List<Meter> _meters = [];

    public Meter Create(MeterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var meter = new Meter(new MeterOptions(options.Name) { Version = options.Version, Tags = options.Tags, Scope = this });
        _meters.Add(meter);
        return meter;
    }

    public void Dispose()
    {
        foreach (var meter in _meters)
        {
            meter.Dispose();
        }

        _meters.Clear();
    }
}
