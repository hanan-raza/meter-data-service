namespace MeterDataService.Application.Quality;

/// <summary>
/// Data quality of a series over a period (Datenqualität), with the counts it was derived from so a score can
/// always be explained.
/// </summary>
/// <param name="Score">0 (unusable) to 100 (complete, metered, inconspicuous), two decimal places.</param>
/// <param name="ExpectedIntervals">Quarter hours in the period: 92, 96 or 100 per German calendar day.</param>
/// <param name="Measured">Intervals with a metered value.</param>
/// <param name="Replaced">Intervals with a substitute value (Ersatzwert) derived by an algorithm.</param>
/// <param name="Estimated">Intervals holding only a placeholder (vorläufiger Wert).</param>
/// <param name="Missing">Intervals with no value at all.</param>
/// <param name="Anomalies">Anomaly detector findings in the period.</param>
public sealed record DataQualityScore(
    decimal Score,
    int ExpectedIntervals,
    int Measured,
    int Replaced,
    int Estimated,
    int Missing,
    int Anomalies);
