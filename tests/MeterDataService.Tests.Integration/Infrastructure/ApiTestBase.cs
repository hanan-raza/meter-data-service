using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MeterDataService.Tests.Integration.Infrastructure;

/// <summary>
/// Runs the real API in memory against the class's PostgreSQL container. The factory runs in the Development
/// environment, so the sample market location from <c>appsettings.Development.json</c> is seeded on start-up.
/// </summary>
public abstract class ApiTestBase : IntegrationTestBase, IDisposable
{
    /// <summary>Same enum handling as the API, so responses deserialize into the application's records.</summary>
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    protected ApiTestBase(PostgreSqlFixture database)
        : base(database)
    {
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:MeterData", database.ConnectionString));
    }

    protected WebApplicationFactory<Program> Factory { get; }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            Factory.Dispose();
        }
    }
}
