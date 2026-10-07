namespace MeterDataService.Domain;

/// <summary>
/// How a substitute value (Ersatzwert) was derived: the algorithm and the inputs it used.
/// Created only through the factory methods, so each method always carries the inputs it needs.
/// </summary>
public sealed record ReplacementTrace
{
    private ReplacementTrace(ReplacementMethod method, decimal? anchorValueBefore, decimal? anchorValueAfter)
    {
        Method = method;
        AnchorValueBefore = anchorValueBefore;
        AnchorValueAfter = anchorValueAfter;
    }

    public ReplacementMethod Method { get; }

    /// <summary>Energy in kWh of the last usable interval before the gap.</summary>
    public decimal? AnchorValueBefore { get; }

    /// <summary>Energy in kWh of the first usable interval after the gap.</summary>
    public decimal? AnchorValueAfter { get; }

    public static ReplacementTrace LinearInterpolation(decimal anchorValueBefore, decimal anchorValueAfter) =>
        new(ReplacementMethod.LinearInterpolation, anchorValueBefore, anchorValueAfter);
}
