using MeterDataService.Application.Aggregation;
using MeterDataService.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MeterDataService.Api.Controllers;

/// <summary>Energy totals of a market location over a period, with how complete and how measured the data is.</summary>
[ApiController]
[Route("api/market-locations/{maLoId}/consumption")]
public sealed class ConsumptionController(ConsumptionQueryService queries) : ControllerBase
{
    /// <summary>Returns the energy of a market location for a period of German calendar days, summed per hour, day or month.</summary>
    /// <remarks>
    /// Sums are exact decimals computed in PostgreSQL. Days and months start at local midnight (Europe/Berlin), so the
    /// fall-back day is one 25-hour bucket with 100 intervals. <c>completenessPercent</c> compares the stored values with
    /// the intervals the period has (92 / 96 / 100 per day), <c>measuredPercent</c> counts only <c>Measured</c> ones.
    /// A bucket at the edge of the period covers only the period's days: a <c>Month</c> report from the 10th to the
    /// 20th holds those 11 days. For a generation location (PV feed-in), the totals are energy fed in.
    /// Longest periods: 92 days (Hour), 1098 days (Day, Month).
    /// </remarks>
    /// <param name="maLoId">11-digit MaLo-ID with a valid check digit.</param>
    /// <param name="from">First German calendar day, inclusive (<c>yyyy-MM-dd</c>).</param>
    /// <param name="to">Last German calendar day, inclusive (<c>yyyy-MM-dd</c>).</param>
    /// <param name="granularity">Bucket length: <c>Hour</c>, <c>Day</c> (default) or <c>Month</c>.</param>
    /// <param name="cancellationToken">Aborts the query.</param>
    /// <response code="200">The report; without stored values the total is 0 and <c>totals</c> is empty.</response>
    /// <response code="400">Malformed MaLo-ID, missing or reversed dates, <c>QuarterHour</c> or unknown granularity, or a period that is too long.</response>
    /// <response code="404">No market location with this ID.</response>
    [HttpGet]
    [ProducesResponseType<ConsumptionReport>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        string maLoId,
        [FromQuery, BindRequired] DateOnly from,
        [FromQuery, BindRequired] DateOnly to,
        [FromQuery] AggregationGranularity granularity = AggregationGranularity.Day,
        CancellationToken cancellationToken = default)
    {
        if (!MarketLocationId.IsValid(maLoId))
        {
            ModelState.AddModelError(nameof(maLoId), $"'{maLoId}' is not a valid MaLo-ID (11 digits with check digit).");
        }

        if (granularity == AggregationGranularity.QuarterHour)
        {
            ModelState.AddModelError(nameof(granularity), "Consumption is summed per Hour, Day or Month; use /measurements for 15-minute values.");
        }
        else if (ReportingPeriod.Validate(from, to, granularity) is { } periodError)
        {
            ModelState.AddModelError(nameof(to), periodError);
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return await queries.GetReportAsync(maLoId, from, to, granularity, cancellationToken) is { } report
            ? Ok(report)
            : Problem(statusCode: StatusCodes.Status404NotFound, title: "Unknown market location", detail: $"No market location '{maLoId}'.");
    }
}
