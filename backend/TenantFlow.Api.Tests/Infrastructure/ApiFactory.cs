using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using TenantFlow.Api.Data;
using TenantFlow.Api.Configuration;

namespace TenantFlow.Api.Tests.Infrastructure;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string connectionString;
    private readonly PostgresFixture owner;
    private readonly string signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    public FrozenClock Clock { get; } = new();
    public ApiFactory(PostgresFixture owner, string connectionString)
    {
        owner.AssertOwned(connectionString);
        this.owner = owner;
        this.connectionString = connectionString;
    }

    public void AssertTestConfiguration()
    {
        if (Services.GetRequiredService<JwtSettings>().Key != signingKey
            || !Services.GetRequiredService<IWebHostEnvironment>().IsEnvironment("Testing"))
            throw new InvalidOperationException("Test-only configuration was not applied. Refusing to run the integration host.");
    }

    private Dictionary<string, string?> TestSettings => new()
    {
        [HostDefaults.EnvironmentKey] = "Testing",
        ["ConnectionStrings:DefaultConnection"] = connectionString,
        ["Jwt:Key"] = signingKey,
        ["Jwt:Issuer"] = "TenantFlow.IntegrationTests",
        ["Jwt:Audience"] = "TenantFlow.IntegrationTests"
    };

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Minimal-host Program reads JWT settings before Build. DeferredHostBuilder
        // supplies host configuration to that entry point before those reads run.
        // Nothing is written to process environment variables or secret files.
        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(TestSettings));
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            // Do not inherit appsettings, user secrets, connection-string env vars or development JWT keys.
            configuration.Sources.Clear();
            configuration.AddInMemoryCollection(TestSettings);
        });
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureTestServices(services =>
        {
            owner.AssertOwned(connectionString);
            services.RemoveAll<ApplicationDbContext>();
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connectionString));
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            // Authentication, Identity, UserManager, password hashing and authorization stay real.
        });
    }
}

public sealed class FrozenClock : TimeProvider
{
    private readonly DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    public override DateTimeOffset GetUtcNow() => now;
}
