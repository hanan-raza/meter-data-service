namespace MeterDataService.Domain;

/// <summary>
/// Finding of an anomaly detector attached to one interval of a series (Auffälligkeitsvermerk). The interval value
/// itself is left alone: an anomaly is a hint for a person to check, not proof that the value is wrong.
/// </summary>
public sealed class SeriesAnnotation
{
    /// <summary>Decimal places persisted for <see cref="Score"/>.</summary>
    public const int ScoreScale = 5;

    // Required by EF Core for materialization.
    private SeriesAnnotation()
    {
    }

    internal SeriesAnnotation(Guid measurementSeriesId, DateTimeOffset intervalStart, AnomalyType type, decimal score)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown anomaly type.");
        }

        if (score is < 0m or > 1m || decimal.Round(score, ScoreScale) != score)
        {
            throw new ArgumentOutOfRangeException(
                nameof(score), score, $"Score must be between 0 and 1 with at most {ScoreScale} decimal places.");
        }

        MeasurementSeriesId = measurementSeriesId;
        IntervalStart = intervalStart.ToUniversalTime();
        Type = type;
        Score = score;
    }

    public long Id { get; private set; }

    public Guid MeasurementSeriesId { get; private set; }

    /// <summary>UTC start of the interval the finding refers to.</summary>
    public DateTimeOffset IntervalStart { get; private set; }

    public AnomalyType Type { get; private set; }

    /// <summary>Detector confidence from 0 to 1: one minus the p-value of the interval under the recent history.</summary>
    public decimal Score { get; private set; }
}
