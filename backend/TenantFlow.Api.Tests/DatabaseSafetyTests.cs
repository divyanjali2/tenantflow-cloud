using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TenantFlow.Api.Data;
using TenantFlow.Api.Tests.Infrastructure;

namespace TenantFlow.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class DatabaseSafetyTests(PostgresFixture postgres) : ApiTest(postgres)
{
    [Fact]
    public async Task TestDatabaseUsesPostgresMigrationsAndCannotTargetAnUnownedDatabase()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Database.ProviderName.Should().Be("Npgsql.EntityFrameworkCore.PostgreSQL");
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await db.Database.GetAppliedMigrationsAsync()).Should().Contain(migration => migration.EndsWith("AddProjectTasks"));
        (await db.Users.CountAsync()).Should().Be(0);
        var candidate = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()!);
        candidate.Database = $"unowned_{Guid.NewGuid():N}";
        Action verify = () => DatabaseOwner.AssertOwned(candidate.ConnectionString);
        verify.Should().Throw<InvalidOperationException>();
    }
}
