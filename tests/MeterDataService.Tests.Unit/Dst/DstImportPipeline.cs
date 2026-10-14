using System.Globalization;
using System.Text;
using System.Threading.Channels;
using MeterDataService.Application.GapFilling;
using MeterDataService.Application.Import;
using MeterDataService.Domain;
using MeterDataService.Infrastructure.AnomalyDetection;
using MeterDataService.Tests.Unit.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace MeterDataService.Tests.Unit.Dst;

/// <summary>
/// Runs a CSV file through the real import pipeline (parser, validation, gap filling, ML.NET anomaly detection,
/// quality score) with only persistence faked, so DST tests cover every step a real upload takes.
/// </summary>
internal static class DstImportPipeline
{
    // ML.NET builds its pipelines on first use; generous so a cold test run doesn't time out.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public static async Task<(ImportJobStatus Status, MeasurementSeries Series)> ImportAsync(string csv)
    {
        var tracker = new ImportJobTracker(new FakeTimeProvider());
        var repository = new FakeMeasurementSeriesRepository();
        using var meterFactory = new FakeMeterFactory();
        await using var services = new ServiceCollection()
            .AddScoped<IMeasurementSeriesRepository>(_ => repository)
            .BuildServiceProvider();

        var queue = Channel.CreateUnbounded<ImportJob>();
        var job = ImportChannelTests.Job(csv);
        tracker.MarkQueued(job);
        queue.Writer.TryWrite(job).ShouldBeTrue();
        queue.Writer.Complete();

        var channel = new ImportChannel(queue);
        using var service = new ImportBackgroundService(
            channel,
            tracker,
            new ValidationEngine(),
            new GapFillingService(NullLogger<GapFillingService>.Instance),
            new AnomalyDetector(NullLogger<AnomalyDetector>.Instance),
            services.GetRequiredService<IServiceScopeFactory>(),
            new ImportMetrics(meterFactory, channel),
            NullLogger<ImportBackgroundService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask.ShouldNotBeNull().WaitAsync(Timeout);

        return (tracker.Find(job.Id).ShouldNotBeNull(), repository.Series);
    }

    /// <summary>CSV with German local timestamps and no offset, the way many exports deliver them.</summary>
    public static string LocalTimeCsv(IEnumerable<(DateTime Local, decimal Value)> rows)
    {
        var csv = new StringBuilder("timestamp,value,status\n");
        foreach (var (local, value) in rows)
        {
            csv.Append(CultureInfo.InvariantCulture, $"{local:yyyy-MM-dd HH:mm},{value},Measured\n");
        }

        return csv.ToString();
    }

    /// <summary>CSV with the explicit offset in effect at each interval, so every row is unambiguous.</summary>
    public static string OffsetCsv(IEnumerable<(DateTimeOffset Start, decimal Value)> rows)
    {
        var csv = new StringBuilder("timestamp,value,status\n");
        foreach (var (start, value) in rows)
        {
            csv.Append(CultureInfo.InvariantCulture, $"{GermanCalendar.ToGermanTime(start):yyyy-MM-dd'T'HH:mm:sszzz},{value},Measured\n");
        }

        return csv.ToString();
    }

    /// <summary>The UTC starts of every quarter hour of a German calendar day: 92, 96 or 100 of them.</summary>
    public static List<DateTimeOffset> QuarterHoursOf(DateOnly day)
    {
        var starts = new List<DateTimeOffset>();
        var end = GermanCalendar.StartOfDayUtc(day.AddDays(1));
        for (var start = GermanCalendar.StartOfDayUtc(day); start < end; start += MeasurementSeries.IntervalLength)
        {
            starts.Add(start);
        }

        return starts;
    }
}
