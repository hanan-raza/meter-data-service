using MeterDataService.Domain;

namespace MeterDataService.Application.Quality;

/// <summary>
/// Rates a series over whole German calendar days on a 0–100 scale. Metered completeness dominates because
/// billing is only as good as the share of real readings; gap filling and anomalies refine the picture.
/// </summary>
/// <remarks>
/// Pure decimal arithmetic over counts, so the same series always yields the same score.
/// <list type="bullet">
/// <item><see cref="MeasuredPoints"/> × share of intervals with a metered value.</item>
/// <item><see cref="GapFillingPoints"/> × share of non-metered intervals that got an algorithmic substitute.
/// A placeholder or a still missing value earns nothing. Full points when nothing needed filling.</item>
/// <item><see cref="AnomalyPoints"/> × share of intervals the detector could inspect (metered or replaced),
/// losing a share per anomaly per day, gone at <see cref="AnomaliesPerDayForNoPoints"/> anomalies per day.
/// Without the coverage factor an empty day would earn points for being inconspicuous.</item>
/// </list>
/// </remarks>
public static class DataQualityScorer
{
    public const decimal MeasuredPoints = 70m;
    public const decimal GapFillingPoints = 20m;
    public const decimal AnomalyPoints = 10m;
    public const decimal AnomaliesPerDayForNoPoints = 4m;

    public static DataQualityScore Score(MeasurementSeries series, DateOnly firstDay, DateOnly lastDay)
    {
        ArgumentNullException.ThrowIfNull(series);
        if (lastDay < firstDay)
        {
            throw new ArgumentException("The last day must not be before the first day.", nameof(lastDay));
        }

        var from = GermanCalendar.StartOfDayUtc(firstDay);
        var to = GermanCalendar.StartOfDayUtc(lastDay.AddDays(1));
        var days = lastDay.DayNumber - firstDay.DayNumber + 1;
        var expected = (int)((to - from) / MeasurementSeries.IntervalLength);

        var values = series.Values.Where(v => v.IntervalStart >= from && v.IntervalStart < to).ToList();
        var measured = values.Count(v => v.Status == MeasurementStatus.Measured);
        var replaced = values.Count(v => v.Status == MeasurementStatus.Replaced);
        var estimated = values.Count(v => v.Status == MeasurementStatus.Estimated);
        var missing = expected - values.Count;
        var anomalies = series.Annotations.Count(a => a.IntervalStart >= from && a.IntervalStart < to);

        var notMeasured = expected - measured;
        var measuredShare = (decimal)measured / expected;
        var filledShare = notMeasured == 0 ? 1m : (decimal)replaced / notMeasured;
        var inspectedShare = (decimal)(measured + replaced) / expected;
        var inconspicuousShare = Math.Max(0m, 1m - (anomalies / (decimal)days / AnomaliesPerDayForNoPoints));

        var score = (MeasuredPoints * measuredShare)
            + (GapFillingPoints * filledShare)
            + (AnomalyPoints * inspectedShare * inconspicuousShare);
        return new DataQualityScore(
            decimal.Round(score, 2, MidpointRounding.AwayFromZero),
            expected,
            measured,
            replaced,
            estimated,
            missing,
            anomalies);
    }
}
