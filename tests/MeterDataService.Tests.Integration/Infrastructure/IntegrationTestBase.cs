using MeterDataService.Infrastructure.Persistence;

namespace MeterDataService.Tests.Integration.Infrastructure;

/// <summary>
/// Base class for tests that need a real PostgreSQL database. Every derived test class gets its own
/// container with all migrations applied (see <see cref="PostgreSqlFixture"/>).
/// </summary>
/// <remarks>
/// Without Docker, tests are skipped locally so <c>dotnet test</c> stays usable, but they fail on CI
/// (<c>CI</c> environment variable set): a skipped integration suite there would hide a broken pipeline.
/// Use <see cref="SkippableFactAttribute"/> instead of <see cref="FactAttribute"/> in derived classes.
/// </remarks>
public abstract class IntegrationTestBase : IClassFixture<PostgreSqlFixture>
{
    protected IntegrationTestBase(PostgreSqlFixture database)
    {
        ArgumentNullException.ThrowIfNull(database);

        Database = database;

        if (database.UnavailableReason is { } reason)
        {
            if (IsRunningOnCi)
            {
                throw new InvalidOperationException($"Integration tests require Docker on CI. {reason}");
            }

            Skip.If(true, $"Docker is not available: {reason}");
        }
    }

    protected PostgreSqlFixture Database { get; }

    /// <summary>Use a new context per unit of work so reads hit the database, not the change tracker.</summary>
    protected MeterDataDbContext CreateDbContext() => Database.CreateDbContext();

    private static bool IsRunningOnCi =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"));
}
