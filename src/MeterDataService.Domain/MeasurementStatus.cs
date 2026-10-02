namespace MeterDataService.Domain;

/// <summary>
/// Origin of a single interval value (Messwertstatus). Downstream billing must know whether
/// a value was actually metered or substituted, so the status travels with every value.
/// </summary>
public enum MeasurementStatus
{
    /// <summary>Value read from the meter (wahrer Wert / gemessen).</summary>
    Measured = 1,

    /// <summary>Value estimated without a usable reference, e.g. no similar day available (vorläufiger Wert / geschätzt).</summary>
    Estimated = 2,

    /// <summary>Value substituted by a gap-filling algorithm (Ersatzwert).</summary>
    Replaced = 3,
}
