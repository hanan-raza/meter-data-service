using MeterDataService.Domain;

namespace MeterDataService.Application.GapFilling;

/// <summary>
/// Run of consecutive 15-minute intervals without a usable value (Messlücke), either missing or rejected
/// by plausibility checks. Bounds are UTC, so a gap across a clock change still has its true length.
/// </summary>
/// <param name="From">Inclusive start of the first unusable interval.</param>
/// <param name="To">Exclusive end of the last unusable interval.</param>
/// <param name="AnchorBefore">Usable value of the interval ending at <paramref name="From"/>, if there is one.</param>
/// <param name="AnchorAfter">Usable value of the interval starting at <paramref name="To"/>, if there is one.</param>
public sealed record Gap(DateTimeOffset From, DateTimeOffset To, decimal? AnchorBefore, decimal? AnchorAfter)
{
    public int IntervalCount => (int)((To - From).Ticks / MeasurementSeries.IntervalLength.Ticks);

    public IEnumerable<DateTimeOffset> IntervalStarts()
    {
        for (var start = From; start < To; start += MeasurementSeries.IntervalLength)
        {
            yield return start;
        }
    }
}
