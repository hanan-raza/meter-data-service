using MeterDataService.Application.Quality;
using MeterDataService.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace MeterDataService.Infrastructure.AnomalyDetection;

/// <summary>
/// ML.NET anomaly detection on a load profile (Lastgang): IID spike detection for single outliers and IID
/// change-point detection for lasting level shifts.
/// </summary>
/// <remarks>
/// <para>
/// Spikes are judged on the raw values against a sliding one-day history: when today's evening peak arrives,
/// yesterday's is still in the window, so the daily shape itself is not flagged.
/// </para>
/// <para>
/// Change points are judged on the difference to the same local time one week earlier. The martingale behind
/// change-point detection only reacts within a short window (a few hours), and inside such a window a raw load
/// profile is all ramps: every morning would be a "change". The week-over-week difference removes the daily and
/// weekday shape and leaves noise around zero until the level really moves. Without a week of history there is
/// no difference to judge, so change points need seven days of values before the period.
/// </para>
/// <para>
/// Estimated placeholders are left out of the input; a block of zero-fallback values would otherwise be reported
/// as a load anomaly when it is really a data gap, which the quality score already accounts for.
/// </para>
/// </remarks>
public sealed partial class AnomalyDetector(ILogger<AnomalyDetector> logger) : IAnomalyDetector
{
    /// <summary>Confidence in percent an interval must exceed to count as a spike.</summary>
    public const double SpikeConfidence = 99.0;

    /// <summary>Intervals of history each value is judged against for spikes: one normal day.</summary>
    public const int SpikeHistoryLength = GermanCalendar.QuarterHoursPerNormalDay;

    /// <summary>Confidence in percent a level shift must exceed to count as a change point.</summary>
    public const double ChangePointConfidence = 95.0;

    /// <summary>Intervals the change-point martingale accumulates over: three hours.</summary>
    /// <remarks>Tuned on synthetic profiles: a day-long window lets the martingale decay so far that a tripled
    /// load never raises an alert; three hours catches a 1.5× shift within two intervals and raised about one
    /// false alarm per hundred clean days.</remarks>
    public const int ChangePointHistoryLength = 12;

    public IReadOnlyList<SeriesAnnotation> Annotate(MeasurementSeries series, DateOnly firstDay, DateOnly lastDay)
    {
        ArgumentNullException.ThrowIfNull(series);
        if (lastDay < firstDay)
        {
            throw new ArgumentException("The last day must not be before the first day.", nameof(lastDay));
        }

        var from = GermanCalendar.StartOfDayUtc(firstDay);
        var to = GermanCalendar.StartOfDayUtc(lastDay.AddDays(1));
        series.ClearAnnotations(from, to);

        var values = series.Values
            .Where(v => v.Status != MeasurementStatus.Estimated && v.IntervalStart < to)
            .OrderBy(v => v.IntervalStart)
            .ToList();
        if (!values.Any(v => v.IntervalStart >= from))
        {
            return [];
        }

        var annotations = new List<SeriesAnnotation>();
        var spikeInput = values.Select(v => (v.IntervalStart, Value: (float)v.Value)).ToList();
        foreach (var (intervalStart, pValue) in Alerts(spikeInput, DetectSpikes))
        {
            if (intervalStart >= from)
            {
                annotations.Add(series.Annotate(intervalStart, AnomalyType.Spike, ScoreOf(pValue)));
            }
        }

        foreach (var (intervalStart, pValue) in Alerts(WeekOverWeekDifferences(values), DetectChangePoints))
        {
            if (intervalStart >= from)
            {
                annotations.Add(series.Annotate(intervalStart, AnomalyType.ChangePoint, ScoreOf(pValue)));
            }
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            var spikes = annotations.Count(a => a.Type == AnomalyType.Spike);
            LogAnomaliesFound(series.Id, firstDay, lastDay, spikes, annotations.Count - spikes);
        }

        return annotations;
    }

    /// <summary>Each value minus the value at the same German local time seven days earlier, where both exist.</summary>
    private static List<(DateTimeOffset IntervalStart, float Value)> WeekOverWeekDifferences(List<MeasurementValue> values)
    {
        var byStart = values.ToDictionary(v => v.IntervalStart, v => v.Value);
        var differences = new List<(DateTimeOffset, float)>(values.Count);
        foreach (var value in values)
        {
            if (SameLocalTimeOneWeekEarlier(value.IntervalStart) is { } reference
                && byStart.TryGetValue(reference, out var referenceValue))
            {
                differences.Add((value.IntervalStart, (float)(value.Value - referenceValue)));
            }
        }

        return differences;
    }

    // By local time, not by UTC minus 168 hours: in the week after a DST switch the latter would compare
    // 18:00 with 17:00 and report the evening ramp as a change.
    private static DateTimeOffset? SameLocalTimeOneWeekEarlier(DateTimeOffset utc)
    {
        var earlier = GermanCalendar.ToGermanTime(utc).DateTime.AddDays(-7);
        if (GermanCalendar.TimeZone.IsInvalidTime(earlier))
        {
            // The skipped hour of the spring-forward day: there is no such interval to compare with.
            return null;
        }

        // In the repeated hour of the fall-back day this picks the standard-time occurrence.
        return new DateTimeOffset(earlier, GermanCalendar.TimeZone.GetUtcOffset(earlier)).ToUniversalTime();
    }

    private static List<(DateTimeOffset IntervalStart, double PValue)> Alerts(
        List<(DateTimeOffset IntervalStart, float Value)> input,
        Func<MLContext, IEstimator<ITransformer>> detector)
    {
        if (input.Count == 0)
        {
            return [];
        }

        // Fixed seed: the same series must always produce the same findings, or the quality score would wobble.
        var ml = new MLContext(seed: 0);
        var data = ml.Data.LoadFromEnumerable(input.Select(p => new LoadPoint { Value = p.Value }));
        var transformed = detector(ml).Fit(data).Transform(data);

        // Output vector: [alert, raw score, p-value, (martingale value for change points)].
        return ml.Data.CreateEnumerable<Prediction>(transformed, reuseRowObject: false)
            .Select((prediction, i) => (input[i].IntervalStart, prediction.Output))
            .Where(p => p.Output[0] > 0)
            .Select(p => (p.IntervalStart, p.Output[2]))
            .ToList();
    }

    private static IEstimator<ITransformer> DetectSpikes(MLContext ml) =>
        ml.Transforms.DetectIidSpike(
            outputColumnName: nameof(Prediction.Output),
            inputColumnName: nameof(LoadPoint.Value),
            confidence: SpikeConfidence,
            pvalueHistoryLength: SpikeHistoryLength);

    private static IEstimator<ITransformer> DetectChangePoints(MLContext ml) =>
        ml.Transforms.DetectIidChangePoint(
            outputColumnName: nameof(Prediction.Output),
            inputColumnName: nameof(LoadPoint.Value),
            confidence: ChangePointConfidence,
            changeHistoryLength: ChangePointHistoryLength);

    private static decimal ScoreOf(double pValue)
    {
        var p = double.IsFinite(pValue) ? Math.Clamp(pValue, 0, 1) : 1;
        return decimal.Round((decimal)(1 - p), SeriesAnnotation.ScoreScale, MidpointRounding.AwayFromZero);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Anomaly detection on series {SeriesId} for {FirstDay}–{LastDay}: {Spikes} spikes, {ChangePoints} change points")]
    private partial void LogAnomaliesFound(Guid seriesId, DateOnly firstDay, DateOnly lastDay, int spikes, int changePoints);

    private sealed class LoadPoint
    {
        public float Value { get; set; }
    }

    private sealed class Prediction
    {
        // Spike output has three slots, change-point output four; an unsized vector column takes either.
        [VectorType]
        public double[] Output { get; set; } = [];
    }
}
