using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using MeterDataService.Application.Import;
using MeterDataService.Domain;
using MeterDataService.Infrastructure.Persistence;
using MeterDataService.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace MeterDataService.Tests.Integration.Api;

[Trait("Category", "Integration")]
public sealed class ImportEndpointTests(PostgreSqlFixture database) : ApiTestBase(database)
{
    private const string ValidMaLo = SampleMasterData.MaLoId;

    [SkippableFact]
    public async Task Upload_returns_202_with_location_and_the_job_is_processed_in_the_background()
    {
        using var client = Factory.CreateClient();

        using var response = await client.PostAsync(new Uri("/api/import", UriKind.Relative), Form(OneDayCsv(), ValidMaLo));

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var queued = (await response.Content.ReadFromJsonAsync<ImportJobStatus>(Json)).ShouldNotBeNull();
        queued.State.ShouldBe(ImportJobState.Queued);
        queued.MarketLocationId.ShouldBe(ValidMaLo);
        queued.FileName.ShouldBe("day.csv");
        response.Headers.Location.ShouldNotBeNull().ToString().ShouldEndWith($"/api/import/{queued.JobId}");

        var finished = await PollUntilFinished(client, response.Headers.Location);
        finished.State.ShouldBe(ImportJobState.Completed);
        finished.Summary.ShouldBe(new ImportSummary(
            RowsRead: 96, ParseErrors: 0, Accepted: 96, Rejected: 0, MissingIntervals: 0, Findings: 0, Substituted: 0));
    }

    [SkippableFact]
    public async Task Documented_sample_file_is_persisted_with_its_gaps_filled()
    {
        using var client = Factory.CreateClient();
        var csv = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Samples", "household-one-day.csv"));

        using var response = await client.PostAsync(new Uri("/api/import", UriKind.Relative), Form(csv, ValidMaLo));

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var finished = await PollUntilFinished(client, response.Headers.Location.ShouldNotBeNull());
        finished.State.ShouldBe(ImportJobState.Completed);

        // The README shows this result: the 18:30 spike is rejected and 03:00 is missing; both are interpolated.
        finished.Summary.ShouldBe(new ImportSummary(
            RowsRead: 95, ParseErrors: 0, Accepted: 94, Rejected: 1, MissingIntervals: 1, Findings: 1, Substituted: 2));

        var day = new DateOnly(2026, 6, 10);
        await using var context = CreateDbContext();
        var stored = await context.MeasurementValues
            .Where(v => v.IntervalStart >= GermanCalendar.StartOfDayUtc(day) && v.IntervalStart < GermanCalendar.StartOfDayUtc(day.AddDays(1)))
            .ToListAsync();
        stored.Count.ShouldBe(96);
        stored.Where(v => v.Status == MeasurementStatus.Replaced)
            .Select(v => TimeZoneInfo.ConvertTime(v.IntervalStart, GermanCalendar.TimeZone).TimeOfDay)
            .ShouldBe([new TimeSpan(3, 0, 0), new TimeSpan(18, 30, 0)], ignoreOrder: true);
        stored.Where(v => v.Status == MeasurementStatus.Replaced).ShouldAllBe(v => v.ReplacedBy == ReplacementMethod.LinearInterpolation);
    }

    [SkippableFact]
    public async Task Import_for_a_market_location_without_master_data_fails_with_a_reason()
    {
        // Valid MaLo-ID, so the endpoint accepts it, but never registered.
        const string unknownMaLo = "10000000009";
        using var client = Factory.CreateClient();

        using var response = await client.PostAsync(new Uri("/api/import", UriKind.Relative), Form(OneDayCsv(), unknownMaLo));

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var finished = await PollUntilFinished(client, response.Headers.Location.ShouldNotBeNull());
        finished.State.ShouldBe(ImportJobState.Failed);
        finished.Error.ShouldBe(ImportBackgroundService.UnknownMarketLocationMessage(unknownMaLo));
    }

    [SkippableTheory]
    [InlineData("41373559242")] // wrong check digit
    [InlineData("4137355924")] // too short
    public async Task Invalid_market_location_id_is_rejected_with_400(string maLo)
    {
        using var client = Factory.CreateClient();

        using var response = await client.PostAsync(new Uri("/api/import", UriKind.Relative), Form(OneDayCsv(), maLo));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("marketLocationId");
    }

    [SkippableFact]
    public async Task Empty_file_is_rejected_with_400()
    {
        using var client = Factory.CreateClient();

        using var response = await client.PostAsync(new Uri("/api/import", UriKind.Relative), Form(string.Empty, ValidMaLo));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task Request_without_file_is_rejected_with_400()
    {
        using var client = Factory.CreateClient();
        using var form = new MultipartFormDataContent { { new StringContent(ValidMaLo), "marketLocationId" } };

        using var response = await client.PostAsync(new Uri("/api/import", UriKind.Relative), form);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task Unknown_job_id_returns_404()
    {
        using var client = Factory.CreateClient();

        using var response = await client.GetAsync(new Uri($"/api/import/{Guid.CreateVersion7()}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [SkippableFact]
    public async Task Full_queue_returns_503_with_retry_after_and_forgets_the_job()
    {
        // No background service draining the queue, and room for exactly one job.
        using var stalled = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.Replace(ServiceDescriptor.Singleton(new ImportChannel(capacity: 1)));
        }));
        using var client = stalled.CreateClient();

        using var first = await client.PostAsync(new Uri("/api/import", UriKind.Relative), Form(OneDayCsv(), ValidMaLo));
        using var second = await client.PostAsync(new Uri("/api/import", UriKind.Relative), Form(OneDayCsv(), ValidMaLo));

        first.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        second.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        second.Headers.RetryAfter.ShouldNotBeNull().Delta.ShouldBe(TimeSpan.FromSeconds(30));
        stalled.Services.GetRequiredService<ImportChannel>().Count.ShouldBe(1);
    }

    private static async Task<ImportJobStatus> PollUntilFinished(HttpClient client, Uri location)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var status = (await client.GetFromJsonAsync<ImportJobStatus>(location, Json)).ShouldNotBeNull();
            if (status.State is ImportJobState.Completed or ImportJobState.Failed || DateTime.UtcNow > deadline)
            {
                return status;
            }

            await Task.Delay(50);
        }
    }

    private static MultipartFormDataContent Form(string csv, string maLo)
    {
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new("text/csv");
        return new MultipartFormDataContent
        {
            { file, "file", "day.csv" },
            { new StringContent(maLo), "marketLocationId" },
        };
    }

    // A different day than the sample file: the database is shared by the class, and a stored value would
    // otherwise turn the sample's gaps into intervals that need no substitute.
    private static string OneDayCsv()
    {
        var csv = new StringBuilder("timestamp;value;status\n");
        for (var local = new DateTime(2026, 5, 20, 0, 0, 0); local.Day == 20; local = local.AddMinutes(15))
        {
            csv.Append(CultureInfo.InvariantCulture, $"{local:yyyy-MM-dd'T'HH:mm:ss}+02:00;0,25;Measured\n");
        }

        return csv.ToString();
    }
}
