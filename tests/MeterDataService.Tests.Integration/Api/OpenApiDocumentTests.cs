using System.Net;
using System.Text.Json;
using MeterDataService.Tests.Integration.Infrastructure;
using Shouldly;

namespace MeterDataService.Tests.Integration.Api;

/// <summary>The published API description: every endpoint is in it, with the XML comments as its documentation.</summary>
[Trait("Category", "Integration")]
public sealed class OpenApiDocumentTests(PostgreSqlFixture database) : ApiTestBase(database)
{
    [SkippableTheory]
    [InlineData("/api/import", "post")]
    [InlineData("/api/import/{id}", "get")]
    [InlineData("/api/market-locations", "get")]
    [InlineData("/api/market-locations/{maLoId}", "get")]
    [InlineData("/api/market-locations/{maLoId}/measurements", "get")]
    [InlineData("/api/market-locations/{maLoId}/consumption", "get")]
    public async Task Every_endpoint_is_documented_with_a_summary_and_its_error_responses(string path, string method)
    {
        using var document = await GetDocumentAsync();

        var operation = document.RootElement.GetProperty("paths").GetProperty(path).GetProperty(method);
        operation.GetProperty("summary").GetString().ShouldNotBeNullOrWhiteSpace();
        var responses = operation.GetProperty("responses").EnumerateObject().Select(r => r.Name).ToList();
        responses.ShouldContain(r => r.StartsWith('2'));
        responses.ShouldContain(r => r.StartsWith('4'));
    }

    [SkippableFact]
    public async Task Query_parameters_and_schemas_carry_their_xml_documentation()
    {
        using var document = await GetDocumentAsync();

        var parameters = document.RootElement.GetProperty("paths").GetProperty("/api/market-locations/{maLoId}/consumption").GetProperty("get")
            .GetProperty("parameters").EnumerateArray().ToList();
        parameters.Single(p => p.GetProperty("name").GetString() == "from").GetProperty("description").GetString()
            .ShouldNotBeNull().ShouldStartWith("First German calendar day, inclusive");

        var report = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("ConsumptionReport");
        report.GetProperty("properties").GetProperty("expectedIntervals").GetProperty("description").GetString()
            .ShouldNotBeNull().ShouldContain("92 / 100");
    }

    [SkippableFact]
    public async Task Scalar_reference_page_is_served_in_development()
    {
        using var client = Factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/scalar/v1", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("text/html");
    }

    private async Task<JsonDocument> GetDocumentAsync()
    {
        using var client = Factory.CreateClient();
        using var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
