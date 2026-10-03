using DotNet.Testcontainers.Builders;
using MeterDataService.Infrastructure;
using MeterDataService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace MeterDataService.Tests.Integration.Infrastructure;

/// <summary>
/// Starts a throwaway PostgreSQL container and applies the EF Core migrations to it. Shared by all
/// tests of one test class through <see cref="IntegrationTestBase"/>, so each class starts from an
/// empty schema while the container start-up cost is paid once per class, not once per test.
/// </summary>
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    // Same major version as docker-compose.yml so tests exercise the production database engine.
    private const string Image = "postgres:17";

    private PostgreSqlContainer? _container;

    public string ConnectionString =>
        _container?.GetConnectionString()
        ?? throw new InvalidOperationException("The PostgreSQL container is not running.");

    /// <summary>Why the container could not be started; <c>null</c> when the database is ready.</summary>
    public string? UnavailableReason { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder(Image).Build();
            await _container.StartAsync();
        }
        catch (DockerUnavailableException ex)
        {
            UnavailableReason = ex.Message;
            return;
        }

        // Migrate instead of EnsureCreated: the tests must run against the schema that ships.
        await using var context = CreateDbContext();
        await context.Database.MigrateAsync();
    }

    public MeterDataDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<MeterDataDbContext>().UseMeterDataStore(ConnectionString).Options);

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
