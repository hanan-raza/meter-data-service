using System.Diagnostics.Metrics;

namespace MeterDataService.Benchmarks;

/// <summary>Creates meters nobody listens to: recording costs what it costs in production without an exporter attached.</summary>
internal sealed class NoOpMeterFactory : IMeterFactory
{
    private readonly List<Meter> _meters = [];

    public Meter Create(MeterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var meter = new Meter(options.Name, options.Version);
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
