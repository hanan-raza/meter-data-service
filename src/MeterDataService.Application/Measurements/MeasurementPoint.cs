using MeterDataService.Application.Aggregation;
using MeterDataService.Domain;

namespace MeterDataService.Application.Measurements;

/// <summary>Energy time series of a market location (Lastgang der Marktlokation) at one granularity.</summary>
/// <param name="MarketLocationId">11-digit MaLo-ID.</param>
/// <param name="ObisCode">OBIS code of the series the values come from.</param>
/// <param name="Granularity">Length of each point.</param>
/// <param name="From">First German calendar day of the period (inclusive).</param>
/// <param name="To">Last German calendar day of the period (inclusive).</param>
/// <param name="Points">Points in time order. Intervals and buckets without any value are left out.</param>
public sealed record MeasurementSeriesView(
    string MarketLocationId,
    string ObisCode,
    AggregationGranularity Granularity,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<MeasurementPoint> Points);

/// <summary>
/// One point of a series: a single 15-minute value, or the sum over an hour, day or month. A 15-minute point also
/// carries its status and, for a substitute this service computed, how it was derived.
/// </summary>
/// <param name="Start">Inclusive start, with the German offset in effect at that instant.</param>
/// <param name="End">Exclusive end, with the German offset in effect at that instant.</param>
/// <param name="EnergyKwh">Energy in kWh.</param>
/// <param name="Intervals">Number of 15-minute values in the point (1 for a quarter hour).</param>
/// <param name="MeasuredIntervals">How many of them are <c>Measured</c>.</param>
/// <param name="Status">Status of a 15-minute value; <c>null</c> for sums.</param>
/// <param name="Substitution">Derivation of a substitute value (Ersatzwert); <c>null</c> otherwise.</param>
public sealed record MeasurementPoint(
    DateTimeOffset Start,
    DateTimeOffset End,
    decimal EnergyKwh,
    int Intervals,
    int MeasuredIntervals,
    MeasurementStatus? Status = null,
    Substitution? Substitution = null)
{
    public static MeasurementPoint From(MeasurementValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new MeasurementPoint(
            GermanCalendar.ToGermanTime(value.IntervalStart),
            GermanCalendar.ToGermanTime(value.IntervalEnd),
            value.Value,
            Intervals: 1,
            MeasuredIntervals: value.Status == MeasurementStatus.Measured ? 1 : 0,
            value.Status,
            value.ReplacedBy is { } method
                ? new Substitution(method, value.AnchorValueBefore, value.AnchorValueAfter, value.SourceDay)
                : null);
    }

    public static MeasurementPoint From(EnergyTotal total)
    {
        ArgumentNullException.ThrowIfNull(total);

        return new MeasurementPoint(total.Start, total.End, total.EnergyKwh, total.Intervals, total.MeasuredIntervals);
    }
}

/// <summary>How a substitute value (Ersatzwert) was derived, so it can be recomputed by hand.</summary>
/// <param name="Method">Algorithm used (Ersatzwertverfahren).</param>
/// <param name="AnchorValueBefore">Linear interpolation: last usable value before the gap, in kWh.</param>
/// <param name="AnchorValueAfter">Linear interpolation: first usable value after the gap, in kWh.</param>
/// <param name="SourceDay">Similar day: the German calendar day the value was copied from (Vergleichstag).</param>
public sealed record Substitution(
    ReplacementMethod Method, decimal? AnchorValueBefore, decimal? AnchorValueAfter, DateOnly? SourceDay);
