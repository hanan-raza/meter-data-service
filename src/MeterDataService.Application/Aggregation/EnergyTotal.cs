namespace MeterDataService.Application.Aggregation;

/// <summary>Length of the buckets energy is summed over.</summary>
public enum AggregationGranularity
{
    /// <summary>Clock hour. The fall-back day has 25 of them, two starting at local 02:00.</summary>
    Hour = 1,

    /// <summary>German calendar day (23, 24 or 25 hours).</summary>
    Day = 2,

    /// <summary>German calendar month, from local midnight on the 1st.</summary>
    Month = 3,
}

/// <summary>Energy of a market location summed over one bucket (Energiemenge je Zeitraum).</summary>
/// <param name="Start">Inclusive start of the bucket, with the German offset in effect at that instant.</param>
/// <param name="End">Exclusive end of the bucket, with the German offset in effect at that instant.</param>
/// <param name="EnergyKwh">Sum of all values in the bucket, in kWh.</param>
/// <param name="Intervals">Number of 15-minute values summed. Less than the bucket holds if values are missing.</param>
/// <param name="MeasuredIntervals">How many of them are <c>Measured</c>; the rest are substitutes or estimates.</param>
public sealed record EnergyTotal(
    DateTimeOffset Start, DateTimeOffset End, decimal EnergyKwh, int Intervals, int MeasuredIntervals);
