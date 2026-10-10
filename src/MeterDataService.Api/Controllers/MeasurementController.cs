using MeterDataService.Application.Aggregation;
using MeterDataService.Application.Measurements;
using MeterDataService.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MeterDataService.Api.Controllers;

/// <summary>The energy time series (Lastgang) of a market location, as stored or summed per hour, day or month.</summary>
[ApiController]
[Route("api/market-locations/{maLoId}/measurements")]
public sealed class MeasurementController(MeasurementQueryService queries) : ControllerBase
{
    /// <summary>Returns the energy series of a market location for a period of German calendar days.</summary>
    /// <remarks>
    /// At <c>QuarterHour</c> each point is one stored value with its status (<c>Measured</c>, <c>Estimated</c>,
    /// <c>Replaced</c>) and, for a substitute this service computed, its derivation. At <c>Hour</c>, <c>Day</c> and
    /// <c>Month</c> points are sums. Days and months start at local midnight; the fall-back day has two 02:00 hours.
    /// Intervals without a value are left out, so missing data is never shown as zero.
    /// Longest periods: 31 days (QuarterHour), 92 days (Hour), 1098 days (Day, Month).
    /// </remarks>
    /// <param name="maLoId">11-digit MaLo-ID with a valid check digit.</param>
    /// <param name="from">First German calendar day, inclusive (<c>yyyy-MM-dd</c>).</param>
    /// <param name="to">Last German calendar day, inclusive (<c>yyyy-MM-dd</c>).</param>
    /// <param name="granularity">Point length: <c>QuarterHour</c> (default), <c>Hour</c>, <c>Day</c> or <c>Month</c>.</param>
    /// <param name="cancellationToken">Aborts the query.</param>
    /// <response code="200">The series; <c>points</c> is empty if nothing is stored in the period.</response>
    /// <response code="400">Malformed MaLo-ID, missing or reversed dates, unknown granularity, or a period that is too long.</response>
    /// <response code="404">No market location with this ID.</response>
    [HttpGet]
    [ProducesResponseType<MeasurementSeriesView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        string maLoId,
        [FromQuery, BindRequired] DateOnly from,
        [FromQuery, BindRequired] DateOnly to,
        [FromQuery] AggregationGranularity granularity = AggregationGranularity.QuarterHour,
        CancellationToken cancellationToken = default)
    {
        if (!MarketLocationId.IsValid(maLoId))
        {
            ModelState.AddModelError(nameof(maLoId), $"'{maLoId}' is not a valid MaLo-ID (11 digits with check digit).");
        }

        if (ReportingPeriod.Validate(from, to, granularity) is { } periodError)
        {
            ModelState.AddModelError(nameof(to), periodError);
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return await queries.GetSeriesAsync(maLoId, from, to, granularity, cancellationToken) is { } series
            ? Ok(series)
            : Problem(statusCode: StatusCodes.Status404NotFound, title: "Unknown market location", detail: $"No market location '{maLoId}'.");
    }
}
