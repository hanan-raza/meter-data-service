using System.Globalization;
using System.Text;
using System.Threading.Channels;
using MeterDataService.Application.GapFilling;
using MeterDataService.Application.Import;
using MeterDataService.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace MeterDataService.Tests.Unit.Application;

public sealed class ImportBackgroundServiceTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SummerTime = TimeSpan.FromHours(2);

    private readonly ImportJobTracker _tracker = new(new FakeTimeProvider());

    // Completing the fake channel ends ReadAllAsync, so the service stops by itself once every job is handled.
    private readonly Channel<ImportJob> _fakeChannel = Channel.CreateUnbounded<ImportJob>();

    private readonly FakeMeasurementSeriesRepository _repository = new();
    private readonly ServiceProvider _services;

    public ImportBackgroundServiceTests()
    {
        _services = new ServiceCollection()
            .AddScoped<IMeasurementSeriesRepository>(_ => _repository)
            .BuildServiceProvider();
    }

    public void Dispose() => _services.Dispose();

    [Fact]
    public async Task Queued_job_is_validated_gap_filled_persisted_and_completed_with_a_summary()
    {
        // 2026-06-10 in summer time: 95 of 96 rows (13:00 is missing), a spike at 12:00, and one unreadable row.
        var csv = DayCsv(skip: At(13, 0), spike: At(12, 0));
        csv += "not-a-timestamp,0.25,Measured\n";
        var job = Enqueue(csv);

        await RunUntilChannelIsDrained();

        var status = _tracker.Find(job.Id).ShouldNotBeNull();
        status.State.ShouldBe(ImportJobState.Completed);
        status.Summary.ShouldBe(new ImportSummary(
            RowsRead: 95, ParseErrors: 1, Accepted: 94, Rejected: 1, MissingIntervals: 1, Findings: 1, Substituted: 2));

        _repository.Saves.ShouldBe(1);
        var values = _repository.Series.Values;
        values.Count.ShouldBe(96);
        values.ShouldAllBe(v => v.Value == 0.25m);
        values.Where(v => v.Status == MeasurementStatus.Replaced).Select(v => v.IntervalStart)
            .ShouldBe([At(12, 0), At(13, 0)], ignoreOrder: true);
        values.Single(v => v.IntervalStart == At(12, 0)).ReplacedBy.ShouldBe(ReplacementMethod.LinearInterpolation);
    }

    [Fact]
    public async Task Series_is_loaded_with_the_lookback_days_and_the_closing_anchor()
    {
        Enqueue(DayCsv());

        await RunUntilChannelIsDrained();

        var (maLoId, from, to) = _repository.LastQuery.ShouldNotBeNull();
        maLoId.ShouldBe("41373559241");
        from.ShouldBe(new DateTimeOffset(2026, 5, 27, 0, 0, 0, SummerTime));
        to.ShouldBe(new DateTimeOffset(2026, 6, 11, 0, 15, 0, SummerTime));
    }

    [Fact]
    public async Task Unknown_market_location_fails_the_job_without_saving()
    {
        _repository.Known = false;
        var job = Enqueue(DayCsv());

        await RunUntilChannelIsDrained();

        var status = _tracker.Find(job.Id).ShouldNotBeNull();
        status.State.ShouldBe(ImportJobState.Failed);
        status.Error.ShouldBe(ImportBackgroundService.UnknownMarketLocationMessage("41373559241"));
        _repository.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task Delivered_value_supersedes_an_earlier_substitute()
    {
        _repository.Series.Substitute(At(12, 0), 0.1m, ReplacementTrace.SimilarDay(new DateOnly(2026, 6, 3)));

        Enqueue(DayCsv());
        await RunUntilChannelIsDrained();

        var value = _repository.Series.Values.Single(v => v.IntervalStart == At(12, 0));
        value.Value.ShouldBe(0.25m);
        value.Status.ShouldBe(MeasurementStatus.Measured);
        value.ReplacedBy.ShouldBeNull();
        value.SourceDay.ShouldBeNull();
    }

    [Fact]
    public async Task Rejected_reading_leaves_the_stored_value_untouched()
    {
        _repository.Series.AddValue(At(12, 0), 0.3m, MeasurementStatus.Measured);

        var job = Enqueue(DayCsv(spike: At(12, 0)));
        await RunUntilChannelIsDrained();

        var value = _repository.Series.Values.Single(v => v.IntervalStart == At(12, 0));
        value.Value.ShouldBe(0.3m);
        value.Status.ShouldBe(MeasurementStatus.Measured);
        _tracker.Find(job.Id).ShouldNotBeNull().Summary.ShouldNotBeNull().Substituted.ShouldBe(0);
    }

    [Fact]
    public async Task File_without_header_completes_with_a_parse_error_instead_of_failing()
    {
        var job = Enqueue(string.Empty);

        await RunUntilChannelIsDrained();

        var status = _tracker.Find(job.Id).ShouldNotBeNull();
        status.State.ShouldBe(ImportJobState.Completed);
        status.Summary.ShouldBe(new ImportSummary(0, 1, 0, 0, 0, 0, 0));
        _repository.LastQuery.ShouldBeNull();
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

    private static DateTimeOffset At(int hour, int minute) => new(2026, 6, 10, hour, minute, 0, SummerTime);

    /// <summary>All 96 quarter hours of 2026-06-10 at 0.25 kWh, optionally without one row or with one 5 kWh spike.</summary>
    private static string DayCsv(DateTimeOffset? skip = null, DateTimeOffset? spike = null)
    {
        var csv = new StringBuilder("timestamp,value,status\n");
        for (var start = At(0, 0); start < At(0, 0).AddDays(1); start += MeasurementSeries.IntervalLength)
        {
            if (start == skip)
            {
                continue;
            }

            var value = start == spike ? "5.0" : "0.25";
            csv.Append(CultureInfo.InvariantCulture, $"{start:yyyy-MM-dd'T'HH:mm:sszzz},{value},Measured\n");
        }

        return csv.ToString();
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
        new(
            new ImportChannel(_fakeChannel),
            _tracker,
            new ValidationEngine(),
            new GapFillingService(NullLogger<GapFillingService>.Instance),
            _services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ImportBackgroundService>.Instance);

    /// <summary>Holds one series in memory, as if every value of it had been loaded.</summary>
    private sealed class FakeMeasurementSeriesRepository : IMeasurementSeriesRepository
    {
        public MeasurementSeries Series { get; } = new MarketLocation("41373559241", EnergyDirection.Consumption)
            .AddMeterLocation("DE0001234567890000000000000000001")
            .AddSeries(MeasurementSeries.ConsumedActiveEnergy);

        public bool Known { get; set; } = true;

        public (string MaLoId, DateTimeOffset From, DateTimeOffset To)? LastQuery { get; private set; }

        public int Saves { get; private set; }

        public Task<MeasurementSeries?> FindEnergySeriesAsync(
            string maLoId, DateTimeOffset valuesFrom, DateTimeOffset valuesTo, CancellationToken cancellationToken)
        {
            LastQuery = (maLoId, valuesFrom, valuesTo);
            return Task.FromResult(Known ? Series : null);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            Saves++;
            return Task.CompletedTask;
        }
    }
}
