using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace MeterDataService.Tests.Integration.Infrastructure;

[Trait("Category", "Integration")]
public sealed class MigrationTests(PostgreSqlFixture database) : IntegrationTestBase(database)
{
    [SkippableFact]
    public async Task Fixture_applies_every_migration_to_a_fresh_database()
    {
        await using var context = CreateDbContext();

        var applied = await context.Database.GetAppliedMigrationsAsync();

        applied.ShouldBe(context.Database.GetMigrations());
        (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
    }
}
