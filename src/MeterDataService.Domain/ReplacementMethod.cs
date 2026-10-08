namespace MeterDataService.Domain;

/// <summary>
/// Algorithm that produced a substitute value (Ersatzwertverfahren). Stored with every value this
/// service replaces, so a billing complaint can be answered with how the number came about.
/// </summary>
public enum ReplacementMethod
{
    /// <summary>Straight line between the last value before and the first value after a short gap (lineare Interpolation).</summary>
    LinearInterpolation = 1,

    /// <summary>Copied from the same local time slice of a recent day of the same type (Vergleichstagverfahren).</summary>
    SimilarDay = 2,

    /// <summary>
    /// No similar day was available, so a zero placeholder was written and marked <see cref="MeasurementStatus.Estimated"/>.
    /// It counts as a gap again on the next gap-filling run, so a later delivery can still replace it.
    /// </summary>
    ZeroFallback = 3,
}
