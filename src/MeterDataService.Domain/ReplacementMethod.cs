namespace MeterDataService.Domain;

/// <summary>
/// Algorithm that produced a substitute value (Ersatzwertverfahren). Stored with every value this
/// service replaces, so a billing complaint can be answered with how the number came about.
/// </summary>
public enum ReplacementMethod
{
    /// <summary>Straight line between the last value before and the first value after a short gap (lineare Interpolation).</summary>
    LinearInterpolation = 1,
}
