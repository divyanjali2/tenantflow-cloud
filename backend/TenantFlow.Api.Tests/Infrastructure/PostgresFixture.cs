using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace TenantFlow.Api.Tests.Infrastructure;

[CollectionDefinition(PostgresCollection.Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Isolated PostgreSQL integration tests";
}

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithName($"tenantflow-tests-{Guid.NewGuid():N}")
        .WithDatabase($"tf_suite_{Guid.NewGuid():N}")
        .WithUsername("tf_tests")
        .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)))
        .WithCleanUp(true)
        .Build();
    private readonly HashSet<string> ownedDatabases = [];

    public async Task InitializeAsync()
    {
        try { await postgres.StartAsync(); }
        catch
        {
            await postgres.DisposeAsync();
            throw new InvalidOperationException("Cannot start the isolated PostgreSQL test container. Start Docker with its Linux engine. No external database fallback is permitted.");
        }
    }

    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"tf_case_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{name}\"";
        await command.ExecuteNonQueryAsync();
        ownedDatabases.Add(name);
        return new NpgsqlConnectionStringBuilder(postgres.GetConnectionString())
        {
            Database = name,
            Pooling = false,
            IncludeErrorDetail = false
        }.ConnectionString;
    }

    public void AssertOwned(string connectionString)
    {
        var candidate = new NpgsqlConnectionStringBuilder(connectionString);
        var ownedServer = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString());
        if (!ownedDatabases.Contains(candidate.Database ?? "")
            || !Regex.IsMatch(candidate.Database ?? "", "\\Atf_case_[0-9a-f]{32}\\z")
            || candidate.Host != ownedServer.Host || candidate.Port != ownedServer.Port
            || candidate.Username != ownedServer.Username || candidate.Password != ownedServer.Password)
            throw new InvalidOperationException("Database safety check rejected a database not owned by this test run.");
    }

    public async Task DropDatabaseAsync(string connectionString)
    {
        AssertOwned(connectionString);
        var name = new NpgsqlConnectionStringBuilder(connectionString).Database!;
        // This connection always comes directly from our private container, never an environment variable.
        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE \"{name}\" WITH (FORCE)";
        await command.ExecuteNonQueryAsync();
        ownedDatabases.Remove(name);
    }

    public async Task DisposeAsync() => await postgres.DisposeAsync();
}
