using System.Globalization;
using MeterDataService.Application.Import;
using MeterDataService.Domain;
using Microsoft.AspNetCore.Mvc;

namespace MeterDataService.Api.Controllers;

/// <summary>
/// Accepts CSV files with interval values for a market location (Marktlokation) and queues them for the
/// background import. Processing happens later; the response only confirms the file was taken.
/// </summary>
[ApiController]
[Route("api/import")]
public sealed partial class ImportController(
    ImportChannel channel,
    ImportJobTracker tracker,
    TimeProvider timeProvider,
    ILogger<ImportController> logger) : ControllerBase
{
    // A year of 15-minute values is about 35,000 rows (~1.5 MB); 10 MB leaves room without inviting abuse.
    public const long MaxFileBytes = 10 * 1024 * 1024;

    // A full queue drains within seconds unless the service is overloaded; clients should back off, not hammer.
    private const int RetryAfterSeconds = 30;

    /// <summary>Queues a CSV file (<c>timestamp,value,status</c>) for import.</summary>
    /// <param name="file">The CSV file.</param>
    /// <param name="marketLocationId">11-digit MaLo-ID with a valid check digit.</param>
    /// <param name="cancellationToken">Aborts reading the upload.</param>
    /// <response code="202">Queued; poll the <c>Location</c> header for the outcome.</response>
    /// <response code="400">No file, an empty file, or an invalid MaLo-ID.</response>
    /// <response code="503">The import queue is full; retry after the <c>Retry-After</c> seconds.</response>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxFileBytes)]
    [RequestSizeLimit(MaxFileBytes + 64 * 1024)]
    [ProducesResponseType<ImportJobStatus>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Upload(IFormFile file, [FromForm] string marketLocationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (!MarketLocationId.IsValid(marketLocationId))
        {
            ModelState.AddModelError(nameof(marketLocationId), $"'{marketLocationId}' is not a valid MaLo-ID (11 digits with check digit).");
        }

        if (file.Length == 0)
        {
            ModelState.AddModelError(nameof(file), "The file is empty.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        string content;
        using (var reader = new StreamReader(file.OpenReadStream()))
        {
            content = await reader.ReadToEndAsync(cancellationToken);
        }

        var job = new ImportJob(Guid.CreateVersion7(), marketLocationId, Path.GetFileName(file.FileName), content, timeProvider.GetUtcNow());

        // Tracked before it is queued: the background service may pick it up before this method returns.
        var status = tracker.MarkQueued(job);
        if (!channel.TryEnqueue(job))
        {
            tracker.Forget(job.Id);
            LogQueueFull(job.FileName, marketLocationId);
            Response.Headers.RetryAfter = RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Import queue is full",
                detail: $"Too many imports are waiting. Retry in {RetryAfterSeconds} seconds.");
        }

        LogQueued(job.Id, job.FileName, file.Length, marketLocationId);
        return AcceptedAtAction(nameof(GetStatus), new { id = job.Id }, status);
    }

    /// <summary>Returns the state of an import job and, once processed, its parse and validation counts.</summary>
    /// <response code="200">The job is known.</response>
    /// <response code="404">No job with this id (unknown, or lost with a restart).</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ImportJobStatus>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetStatus(Guid id) =>
        tracker.Find(id) is { } status ? Ok(status) : NotFound();

    [LoggerMessage(Level = LogLevel.Information, Message = "Queued import {ImportJobId} of {FileName} ({Bytes} bytes) for {MarketLocationId}")]
    private partial void LogQueued(Guid importJobId, string fileName, long bytes, string marketLocationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Import queue is full; refused {FileName} for {MarketLocationId}")]
    private partial void LogQueueFull(string fileName, string marketLocationId);
}
