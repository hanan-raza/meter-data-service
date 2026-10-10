using System.ComponentModel.DataAnnotations;
using MeterDataService.Application.MarketLocations;
using MeterDataService.Domain;
using Microsoft.AspNetCore.Mvc;

namespace MeterDataService.Api.Controllers;

/// <summary>Master data (Stammdaten) of market locations (Marktlokationen) and the meter locations that measure them.</summary>
[ApiController]
[Route("api/market-locations")]
public sealed class MarketLocationController(IMarketLocationReadRepository marketLocations) : ControllerBase
{
    public const int MaxPageSize = 500;

    /// <summary>Lists market locations, ordered by MaLo-ID.</summary>
    /// <param name="offset">Number of market locations to skip.</param>
    /// <param name="limit">Page size, 1–500.</param>
    /// <param name="cancellationToken">Aborts the query.</param>
    /// <response code="200">One page of market locations; fewer than <c>limit</c> items means it is the last.</response>
    /// <response code="400"><c>offset</c> or <c>limit</c> out of range.</response>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<MarketLocationDetails>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        [FromQuery, Range(0, int.MaxValue)] int offset = 0,
        [FromQuery, Range(1, MaxPageSize)] int limit = 100,
        CancellationToken cancellationToken = default) =>
        Ok(await marketLocations.ListAsync(offset, limit, cancellationToken));

    /// <summary>Returns one market location with its meter locations, installed meters and series.</summary>
    /// <param name="maLoId">11-digit MaLo-ID with a valid check digit.</param>
    /// <param name="cancellationToken">Aborts the query.</param>
    /// <response code="200">The market location.</response>
    /// <response code="400">The MaLo-ID is malformed or its check digit is wrong.</response>
    /// <response code="404">No market location with this ID.</response>
    [HttpGet("{maLoId}")]
    [ProducesResponseType<MarketLocationDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string maLoId, CancellationToken cancellationToken)
    {
        if (!MarketLocationId.IsValid(maLoId))
        {
            ModelState.AddModelError(nameof(maLoId), $"'{maLoId}' is not a valid MaLo-ID (11 digits with check digit).");
            return ValidationProblem(ModelState);
        }

        return await marketLocations.FindAsync(maLoId, cancellationToken) is { } location
            ? Ok(location)
            : Problem(statusCode: StatusCodes.Status404NotFound, title: "Unknown market location", detail: $"No market location '{maLoId}'.");
    }
}
