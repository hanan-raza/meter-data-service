using System.Globalization;
using System.Text;
using System.Threading.Channels;
using MeterDataService.Application.Import;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace MeterDataService.Tests.Unit.Application;

public class ImportBackgroundServiceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly ImportJobTracker _tracker = new(new FakeTimeProvider());

    // Completing the fake channel ends ReadAllAsync, so the service stops by itself once every job is handled.
    private readonly Channel<ImportJob> _fakeChannel = Channel.CreateUnbounded<ImportJob>();

    [Fact]
    public async Task Queued_job_is_parsed_validated_and_completed_with_a_summary()
    {
        // 2026-06-10 in summer time: 95 of 96 rows (13:00 is missing), a spike at 12:00, and one unreadable row.
        var csv = new StringBuilder("timestamp,value,status\n");
        for (var local = new DateTime(2026, 6, 10, 0, 0, 0); local.Day == 10; local = local.AddMinutes(15))
        {
            if (local.Hour == 13 && local.Minute == 0)
            {
                continue;
            }

            var value = local.Hour == 12 && local.Minute == 0 ? "5.0" : "0.25";
            csv.Append(CultureInfo.InvariantCulture, $"{local:yyyy-MM-dd'T'HH:mm:ss}+02:00,{value},Measured\n");
        }

        csv.Append("not-a-timestamp,0.25,Measured\n");
        var job = Enqueue(csv.ToString());

        await RunUntilChannelIsDrained();

        var status = _tracker.Find(job.Id).ShouldNotBeNull();
        status.State.ShouldBe(ImportJobState.Completed);
        status.Summary.ShouldBe(new ImportSummary(
            RowsRead: 95, ParseErrors: 1, Accepted: 94, Rejected: 1, MissingIntervals: 1, Findings: 1));
    }

    [Fact]
    public async Task File_without_header_completes_with_a_parse_error_instead_of_failing()
    {
        var job = Enqueue(string.Empty);

        await RunUntilChannelIsDrained();

        var status = _tracker.Find(job.Id).ShouldNotBeNull();
        status.State.ShouldBe(ImportJobState.Completed);
        status.Summary.ShouldBe(new ImportSummary(0, 1, 0, 0, 0, 0));
    }

    [Fact]
    public async Task A_job_that_throws_does_not_stop_the_jobs_behind_it()
    {
        // Untracked: marking it as processing throws inside the service.
        var broken = ImportChannelTests.Job();
        _fakeChannel.Writer.TryWrite(broken);
        var next = Enqueue("timestamp,value,status\n");

        await RunUntilChannelIsDrained();

        _tracker.Find(broken.Id).ShouldBeNull();
        _tracker.Find(next.Id).ShouldNotBeNull().State.ShouldBe(ImportJobState.Completed);
    }

    [Fact]
    public async Task Stops_cleanly_when_the_host_shuts_down_while_waiting_for_jobs()
    {
        using var service = CreateService();
        await service.StartAsync(CancellationToken.None);

        await service.StopAsync(CancellationToken.None);

        // Since .NET 10 ExecuteAsync is started via Task.Run with the stopping token: a stop that wins the race
        // against its start leaves the task Canceled. Both outcomes are a clean shutdown; only a fault is not.
        var execution = service.ExecuteTask.ShouldNotBeNull();
        await execution.ContinueWith(_ => { }, TaskScheduler.Default).WaitAsync(Timeout);
        execution.IsFaulted.ShouldBeFalse();
    }

    private ImportJob Enqueue(string content)
    {
        var job = ImportChannelTests.Job(content);
        _tracker.MarkQueued(job);
        _fakeChannel.Writer.TryWrite(job).ShouldBeTrue();
        return job;
    }

    private async Task RunUntilChannelIsDrained()
    {
        _fakeChannel.Writer.Complete();
        using var service = CreateService();

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask.ShouldNotBeNull().WaitAsync(Timeout);
    }

    private ImportBackgroundService CreateService() =>
        new(new ImportChannel(_fakeChannel), _tracker, new ValidationEngine(), NullLogger<ImportBackgroundService>.Instance);
}
