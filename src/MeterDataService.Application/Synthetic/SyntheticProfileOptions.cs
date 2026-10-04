namespace MeterDataService.Application.Synthetic;

/// <summary>
/// Controls the data-quality defects injected by <see cref="SyntheticProfileGenerator"/>. The defaults
/// roughly match what a metering point operator sees from a healthy fleet, so downstream validation
/// and gap filling have something realistic to work on.
/// </summary>
public sealed record SyntheticProfileOptions
{
    /// <summary>Probability that an interval is missing entirely (Messlücke).</summary>
    public double GapProbability { get; init; } = 0.005;

    /// <summary>Probability that an interval value is an implausible outlier (Ausreißer).</summary>
    public double SpikeProbability { get; init; } = 0.002;

    /// <summary>Fixed seed for reproducible series; <c>null</c> produces a different series on every enumeration.</summary>
    public int? Seed { get; init; }
}
