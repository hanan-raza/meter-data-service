using System.Collections.Concurrent;

namespace MeterDataService.Application.Import;

/// <summary>
/// In-memory status of import jobs, shared by the upload endpoint (writes Queued, reads for polling) and
/// the background service (writes the rest). Registered as a singleton.
/// </summary>
/// <remarks>
/// Statuses are lost on restart and are never evicted. That is acceptable while import results aren't
/// persisted yet; once they are, the database becomes the source of truth for finished jobs.
/// </remarks>
public sealed class ImportJobTracker(TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<Guid, ImportJobStatus> _jobs = new();

    public void MarkQueued(ImportJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        var status = new ImportJobStatus(job.Id, job.MarketLocationId, job.FileName, ImportJobState.Queued, job.ReceivedAt);
        if (!_jobs.TryAdd(job.Id, status))
        {
            throw new InvalidOperationException($"Import job {job.Id} is already tracked.");
        }
    }

    public void MarkProcessing(Guid jobId) =>
        Update(jobId, status => status with { State = ImportJobState.Processing });

    public void MarkCompleted(Guid jobId, ImportSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        Update(jobId, status => status with
        {
            State = ImportJobState.Completed,
            FinishedAt = timeProvider.GetUtcNow(),
            Summary = summary,
        });
    }

    public void MarkFailed(Guid jobId, string error) =>
        Update(jobId, status => status with
        {
            State = ImportJobState.Failed,
            FinishedAt = timeProvider.GetUtcNow(),
            Error = error,
        });

    /// <summary>Removes a job that was never queued, e.g. because the channel was full.</summary>
    public void Forget(Guid jobId) => _jobs.TryRemove(jobId, out _);

    public ImportJobStatus? Find(Guid jobId) => _jobs.TryGetValue(jobId, out var status) ? status : null;

    private void Update(Guid jobId, Func<ImportJobStatus, ImportJobStatus> change)
    {
        // Each job has a single writer at a time (endpoint, then background service), so a plain
        // read-modify-write can't lose an update.
        if (!_jobs.TryGetValue(jobId, out var status))
        {
            throw new InvalidOperationException($"Import job {jobId} is not tracked.");
        }

        _jobs[jobId] = change(status);
    }
}
