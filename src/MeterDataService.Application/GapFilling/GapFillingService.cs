using MeterDataService.Domain;
using Microsoft.Extensions.Logging;

namespace MeterDataService.Application.GapFilling;

/// <summary>
/// Substitute value formation (Ersatzwertbildung) for a period: finds the gaps, then runs the fillers
/// from the most local to the most general method, each taking what the previous one left over.
/// </summary>
public sealed partial class GapFillingService(ILogger<GapFillingService> logger)
{
    /// <summary>Fills every gap over whole German calendar days.</summary>
    /// <param name="series">Series to fill; should hold the <see cref="SimilarDayFiller.LookbackDays"/> before <paramref name="firstDay"/>.</param>
    /// <param name="rejectedIntervals">UTC starts of values that failed plausibility checks.</param>
    public GapFillResult Fill(
        MeasurementSeries series,
        DateOnly firstDay,
        DateOnly lastDay,
        IReadOnlySet<DateTimeOffset>? rejectedIntervals = null)
    {
        ArgumentNullException.ThrowIfNull(series);

        var gaps = GapDetector.FindGaps(series, firstDay, lastDay, rejectedIntervals);

        // Interpolation first: next to a short gap the neighbouring values are the best evidence there is.
        // Running similar-day filling first would swallow short gaps with a day-old shape instead.
        var interpolated = LinearInterpolationFiller.Fill(series, gaps);
        var similarDay = SimilarDayFiller.Fill(series, interpolated.Unfilled, rejectedIntervals);

        var written = interpolated.Replaced.Concat(similarDay.Replaced).ToList();
        if (logger.IsEnabled(LogLevel.Information))
        {
            var zeroFallbacks = similarDay.Replaced.Count(v => v.ReplacedBy == ReplacementMethod.ZeroFallback);
            LogGapsFilled(
                series.Id,
                firstDay,
                lastDay,
                gaps.Count,
                interpolated.Replaced.Count,
                similarDay.Replaced.Count - zeroFallbacks,
                zeroFallbacks);
        }

        return new GapFillResult(written, similarDay.Unfilled);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Filled {GapCount} gaps in series {SeriesId} for {FirstDay}–{LastDay}: {Interpolated} interpolated, {SimilarDay} from similar days, {ZeroFallback} zero fallbacks")]
    private partial void LogGapsFilled(
        Guid seriesId,
        DateOnly firstDay,
        DateOnly lastDay,
        int gapCount,
        int interpolated,
        int similarDay,
        int zeroFallback);
}
