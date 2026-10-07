using MeterDataService.Domain;

namespace MeterDataService.Application.GapFilling;

/// <summary>Outcome of one gap-filling step.</summary>
/// <param name="Replaced">Values written into the series, in the order of the gaps passed in.</param>
/// <param name="Unfilled">Gaps this step couldn't fill; the next step in the chain gets them.</param>
public sealed record GapFillResult(IReadOnlyList<MeasurementValue> Replaced, IReadOnlyList<Gap> Unfilled);
