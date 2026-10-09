using MeterDataService.Application.Import;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace MeterDataService.Tests.Unit.Application;

public class ImportJobTrackerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 8, 5, 0, TimeSpan.Zero);

    private readonly ImportJobTracker _tracker = new(new FakeTimeProvider(Now));

    [Fact]
    public void Queued_job_is_found_with_its_metadata()
    {
        var job = ImportChannelTests.Job();

        _tracker.MarkQueued(job);

        var status = _tracker.Find(job.Id).ShouldNotBeNull();
        status.State.ShouldBe(ImportJobState.Queued);
        status.MarketLocationId.ShouldBe(job.MarketLocationId);
        status.FileName.ShouldBe(job.FileName);
        status.ReceivedAt.ShouldBe(job.ReceivedAt);
        status.FinishedAt.ShouldBeNull();
    }

    [Fact]
    public void Completed_job_carries_summary_and_finish_time()
    {
        var job = ImportChannelTests.Job();
        var summary = new ImportSummary(RowsRead: 96, ParseErrors: 0, Accepted: 95, Rejected: 1, MissingIntervals: 0, Findings: 0, Substituted: 1);
        _tracker.MarkQueued(job);
        _tracker.MarkProcessing(job.Id);

        _tracker.MarkCompleted(job.Id, summary);

        var status = _tracker.Find(job.Id).ShouldNotBeNull();
        status.State.ShouldBe(ImportJobState.Completed);
        status.Summary.ShouldBe(summary);
        status.FinishedAt.ShouldBe(Now);
        status.Error.ShouldBeNull();
    }

    [Fact]
    public void Failed_job_carries_the_error()
    {
        var job = ImportChannelTests.Job();
        _tracker.MarkQueued(job);

        _tracker.MarkFailed(job.Id, "boom");

        var status = _tracker.Find(job.Id).ShouldNotBeNull();
        status.State.ShouldBe(ImportJobState.Failed);
        status.Error.ShouldBe("boom");
        status.FinishedAt.ShouldBe(Now);
    }

    [Fact]
    public void Forgotten_job_is_no_longer_found()
    {
        var job = ImportChannelTests.Job();
        _tracker.MarkQueued(job);

        _tracker.Forget(job.Id);

        _tracker.Find(job.Id).ShouldBeNull();
    }

    [Fact]
    public void Same_job_cannot_be_queued_twice()
    {
        var job = ImportChannelTests.Job();
        _tracker.MarkQueued(job);

        Should.Throw<InvalidOperationException>(() => _tracker.MarkQueued(job));
    }

    [Fact]
    public void Updating_an_unknown_job_throws()
    {
        Should.Throw<InvalidOperationException>(() => _tracker.MarkProcessing(Guid.CreateVersion7()));
    }
}
