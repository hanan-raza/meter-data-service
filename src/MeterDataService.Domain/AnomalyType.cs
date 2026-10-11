namespace MeterDataService.Domain;

/// <summary>Kind of irregularity an anomaly detector found in a load profile (Auffälligkeit im Lastgang).</summary>
public enum AnomalyType
{
    /// <summary>A single interval far off the recent level, e.g. a meter glitch or a transmission error (Ausreißer).</summary>
    Spike = 1,

    /// <summary>The level of the series shifts and stays there, e.g. a new consumer, a broken meter or a wrong transformer factor (Strukturbruch).</summary>
    ChangePoint = 2,
}
