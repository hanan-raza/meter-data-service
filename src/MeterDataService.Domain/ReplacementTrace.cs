namespace MeterDataService.Domain;

/// <summary>
/// How a substitute value (Ersatzwert) was derived: the algorithm and the inputs it used.
/// Created only through the factory methods, so each method always carries the inputs it needs.
/// </summary>
public sealed record ReplacementTrace
{
    private ReplacementTrace(
        ReplacementMethod method,
        MeasurementStatus resultingStatus,
        decimal? anchorValueBefore = null,
        decimal? anchorValueAfter = null,
        DateOnly? sourceDay = null)
    {
        Method = method;
        ResultingStatus = resultingStatus;
        AnchorValueBefore = anchorValueBefore;
        AnchorValueAfter = anchorValueAfter;
        SourceDay = sourceDay;
    }

    public ReplacementMethod Method { get; }

    /// <summary>Status the substituted value gets: a derived value is replaced, a placeholder only estimated.</summary>
    public MeasurementStatus ResultingStatus { get; }

    /// <summary>Energy in kWh of the last usable interval before the gap.</summary>
    public decimal? AnchorValueBefore { get; }

    /// <summary>Energy in kWh of the first usable interval after the gap.</summary>
    public decimal? AnchorValueAfter { get; }

    /// <summary>German calendar day the value was copied from (Vergleichstag).</summary>
    public DateOnly? SourceDay { get; }

    public static ReplacementTrace LinearInterpolation(decimal anchorValueBefore, decimal anchorValueAfter) =>
        new(ReplacementMethod.LinearInterpolation, MeasurementStatus.Replaced, anchorValueBefore, anchorValueAfter);

    public static ReplacementTrace SimilarDay(DateOnly sourceDay) =>
        new(ReplacementMethod.SimilarDay, MeasurementStatus.Replaced, sourceDay: sourceDay);

    public static ReplacementTrace ZeroFallback() =>
        new(ReplacementMethod.ZeroFallback, MeasurementStatus.Estimated);
}
