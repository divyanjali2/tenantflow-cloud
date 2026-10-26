using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TenantFlow.Api.Authorization;
using TenantFlow.Api.Data;
using TenantFlow.Api.Models;

namespace TenantFlow.Tests;

public sealed class TenantMembersAuthorizationTests : IClassFixture<TenantApiFactory>
{
    private readonly TenantApiFactory _factory;

    public TenantMembersAuthorizationTests(TenantApiFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData(TenantRoles.Owner)]
    [InlineData(TenantRoles.Admin)]
    public async Task OwnerAndAdminCanListTenantMembers(string role)
    {
        var seed = await _factory.SeedTenantAsync(role);
        using var client = CreateClient(seed.UserId);

        var response = await client.GetAsync($"/api/tenants/{seed.TenantId}/members");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(seed.UserId.ToString(), body);
        Assert.Contains("userId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MemberIsForbiddenFromListingTenantMembers()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Member);
        using var client = CreateClient(seed.UserId);

        var response = await client.GetAsync($"/api/tenants/{seed.TenantId}/members");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task NonMemberReceivesNotFound()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Owner, includeCurrentUserMembership: false);
        using var client = CreateClient(seed.UserId);

        var response = await client.GetAsync($"/api/tenants/{seed.TenantId}/members");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedRequestReceivesUnauthorized()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Owner);
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/tenants/{seed.TenantId}/members");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task OwnerInOneTenantCannotListAnotherTenantsMembers()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Owner, createUnrelatedTenant: true);
        using var client = CreateClient(seed.UserId);

        var response = await client.GetAsync($"/api/tenants/{seed.UnrelatedTenantId}/members");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private HttpClient CreateClient(Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.UserIdHeader, userId.ToString());
        return client;
    }
}

public sealed class TenantApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "unit-test-only-signing-key-that-is-at-least-32-bytes",
                ["Jwt:Issuer"] = "TenantFlow.Tests",
                ["Jwt:Audience"] = "TenantFlow.Tests",
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=unused"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ApplicationDbContext>();
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase("TenantFlowTenantAuthorizationTests"));

            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultForbidScheme = TestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    _ => { });
        });
    }

    public async Task<SeededTenant> SeedTenantAsync(
        string currentUserRole,
        bool includeCurrentUserMembership = true,
        bool createUnrelatedTenant = false)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureCreatedAsync();

        var currentUser = NewUser($"current-{Guid.NewGuid():N}@example.com");
        var otherUser = NewUser($"other-{Guid.NewGuid():N}@example.com");
        var tenant = NewTenant($"tenant-{Guid.NewGuid():N}");
        dbContext.Users.AddRange(currentUser, otherUser);
        dbContext.Tenants.Add(tenant);

        if (includeCurrentUserMembership)
        {
            dbContext.TenantMemberships.Add(NewMembership(currentUser, tenant, currentUserRole));
        }

        dbContext.TenantMemberships.Add(NewMembership(otherUser, tenant, TenantRoles.Member));

        var unrelatedTenantId = Guid.Empty;
        if (createUnrelatedTenant)
        {
            var unrelatedTenant = NewTenant($"tenant-{Guid.NewGuid():N}");
            unrelatedTenantId = unrelatedTenant.Id;
            dbContext.Tenants.Add(unrelatedTenant);
        }

        await dbContext.SaveChangesAsync();
        return new SeededTenant(currentUser.Id, tenant.Id, unrelatedTenantId);
    }

    private static ApplicationUser NewUser(string email) => new()
    {
        Id = Guid.NewGuid(),
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        EmailConfirmed = true
    };

    private static Tenant NewTenant(string slug) => new()
    {
        Id = Guid.NewGuid(),
        Name = slug,
        Slug = slug,
        CreatedAtUtc = DateTime.UtcNow
    };

    private static TenantMembership NewMembership(
        ApplicationUser user,
        Tenant tenant,
        string role) => new()
    {
        Id = Guid.NewGuid(),
        UserId = user.Id,
        TenantId = tenant.Id,
        Role = role,
        CreatedAtUtc = DateTime.UtcNow
    };
}

public sealed record SeededTenant(Guid UserId, Guid TenantId, Guid UnrelatedTenantId);

public sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TenantTests";
    public const string UserIdHeader = "X-Test-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserIdHeader, out var userIdValue)
            || !Guid.TryParse(userIdValue, out var userId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(
            [new Claim("sub", userId.ToString("D"))],
            SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
