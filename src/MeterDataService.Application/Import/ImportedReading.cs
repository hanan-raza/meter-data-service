using MeterDataService.Domain;

namespace MeterDataService.Application.Import;

/// <summary>
/// One parsed CSV row (Messwert aus Importdatei). Kept separate from <see cref="MeasurementValue"/> so that
/// rejected rows stay traceable to their source line without ever reaching a measurement series.
/// </summary>
/// <param name="LineNumber">1-based line in the source file, header included.</param>
/// <param name="IntervalStart">Start of the 15-minute interval, in UTC.</param>
/// <param name="Value">Energy in kWh, as delivered (may be negative).</param>
/// <param name="Status">Status as delivered by the sender.</param>
public sealed record ImportedReading(int LineNumber, DateTimeOffset IntervalStart, decimal Value, MeasurementStatus Status);
