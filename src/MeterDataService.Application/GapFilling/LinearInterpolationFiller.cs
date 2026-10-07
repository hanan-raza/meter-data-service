using MeterDataService.Domain;

namespace MeterDataService.Application.GapFilling;

/// <summary>
/// Fills short gaps by linear interpolation (lineare Interpolation) between the last usable value before
/// and the first usable value after the gap. Within an hour the load rarely changes shape, so a straight
/// line is a defensible substitute; longer gaps are left for similar-day filling (Vergleichstag).
/// </summary>
public static class LinearInterpolationFiller
{
    /// <summary>Longest gap this filler handles: 4 quarter-hours, i.e. one hour.</summary>
    public const int MaxGapIntervals = 4;

    /// <summary>
    /// Fills every gap of at most <see cref="MaxGapIntervals"/> intervals that has a usable anchor on both
    /// sides. Each filled value is <see cref="MeasurementStatus.Replaced"/> and carries both anchors.
    /// </summary>
    public static GapFillResult Fill(MeasurementSeries series, IReadOnlyList<Gap> gaps)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(gaps);

        var replaced = new List<MeasurementValue>();
        var unfilled = new List<Gap>();
        foreach (var gap in gaps)
        {
            // An edge gap has only one anchor; extrapolating from it would invent a trend.
            if (gap.IntervalCount > MaxGapIntervals || gap.AnchorBefore is not { } before || gap.AnchorAfter is not { } after)
            {
                unfilled.Add(gap);
                continue;
            }

            var trace = ReplacementTrace.LinearInterpolation(before, after);
            var steps = gap.IntervalCount + 1;
            var k = 1;
            foreach (var start in gap.IntervalStarts())
            {
                replaced.Add(series.Substitute(start, Interpolate(before, after, k++, steps), trace));
            }
        }

        return new GapFillResult(replaced, unfilled);
    }

    // Rounded to the persisted scale; away from zero is the commercial rounding (kaufmännisches Runden)
    // that billing staff expect when they recompute a value by hand.
    private static decimal Interpolate(decimal before, decimal after, int k, int steps) =>
        decimal.Round(before + ((after - before) * k / steps), MeasurementValue.Scale, MidpointRounding.AwayFromZero);
}
