using System.Net;
using System.Net.Http.Json;
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
using TenantFlow.Api.Contracts;
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

    [Fact]
    public async Task OwnerCanInviteExistingUserUsingNormalizedEmail()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Owner);
        var invitedUser = await _factory.SeedUserAsync($"sarah-{Guid.NewGuid():N}@example.com");
        using var client = CreateClient(seed.UserId);

        var response = await client.PostAsJsonAsync(
            $"/api/tenants/{seed.TenantId}/invitations",
            new { email = invitedUser.Email.ToUpperInvariant(), role = TenantRoles.Member });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(
            $"/api/tenants/{seed.TenantId}/members",
            response.Headers.Location?.OriginalString);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var membership = await dbContext.TenantMemberships
            .SingleAsync(entry => entry.TenantId == seed.TenantId && entry.UserId == invitedUser.UserId);
        Assert.Equal(TenantRoles.Member, membership.Role);
    }

    [Fact]
    public async Task NonOwnerMemberCannotInviteUser()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Member);
        var invitedUser = await _factory.SeedUserAsync($"sarah-{Guid.NewGuid():N}@example.com");
        using var client = CreateClient(seed.UserId);

        var response = await client.PostAsJsonAsync(
            $"/api/tenants/{seed.TenantId}/invitations",
            new { email = invitedUser.Email, role = TenantRoles.Member });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task NonMemberReceivesNotFoundWhenInvitingUser()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Owner, includeCurrentUserMembership: false);
        var invitedUser = await _factory.SeedUserAsync($"sarah-{Guid.NewGuid():N}@example.com");
        using var client = CreateClient(seed.UserId);

        var response = await client.PostAsJsonAsync(
            $"/api/tenants/{seed.TenantId}/invitations",
            new { email = invitedUser.Email, role = TenantRoles.Member });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OwnerCannotAssignOwnerRoleThroughInvitation()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Owner);
        var invitedUser = await _factory.SeedUserAsync($"sarah-{Guid.NewGuid():N}@example.com");
        using var client = CreateClient(seed.UserId);

        var response = await client.PostAsJsonAsync(
            $"/api/tenants/{seed.TenantId}/invitations",
            new { email = invitedUser.Email, role = TenantRoles.Owner });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OwnerReceivesNotFoundWhenInvitingUnknownEmail()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Owner);
        using var client = CreateClient(seed.UserId);

        var response = await client.PostAsJsonAsync(
            $"/api/tenants/{seed.TenantId}/invitations",
            new { email = $"missing-{Guid.NewGuid():N}@example.com", role = TenantRoles.Member });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OwnerReceivesConflictWhenInvitingExistingMember()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Owner);
        var invitedUser = await _factory.SeedUserAsync($"sarah-{Guid.NewGuid():N}@example.com");

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.TenantMemberships.Add(new TenantMembership
            {
                Id = Guid.NewGuid(),
                TenantId = seed.TenantId,
                UserId = invitedUser.UserId,
                Role = TenantRoles.Member
            });
            await dbContext.SaveChangesAsync();
        }

        using var client = CreateClient(seed.UserId);
        var response = await client.PostAsJsonAsync(
            $"/api/tenants/{seed.TenantId}/invitations",
            new { email = invitedUser.Email, role = TenantRoles.Member });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task OwnerCanPromoteMemberToAdmin()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Owner);
        using var client = CreateClient(seed.UserId);

        var response = await client.PatchAsJsonAsync(
            $"/api/tenants/{seed.TenantId}/members/{seed.OtherUserId}/role",
            new { role = TenantRoles.Admin });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updatedMember = await response.Content.ReadFromJsonAsync<TenantMemberResponse>();
        Assert.NotNull(updatedMember);
        Assert.Equal(seed.OtherUserId, updatedMember.UserId);
        Assert.Equal(TenantRoles.Admin, updatedMember.Role);
        Assert.Equal(TenantRoles.Admin, await ReadMembershipRoleAsync(seed.TenantId, seed.OtherUserId));
    }

    [Fact]
    public async Task OwnerCanDemoteAdminToMember()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Owner);
        await SetMembershipRoleAsync(seed.TenantId, seed.OtherUserId, TenantRoles.Admin);
        using var client = CreateClient(seed.UserId);

        var response = await client.PatchAsJsonAsync(
            $"/api/tenants/{seed.TenantId}/members/{seed.OtherUserId}/role",
            new { role = TenantRoles.Member });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TenantRoles.Member, await ReadMembershipRoleAsync(seed.TenantId, seed.OtherUserId));
    }

    [Theory]
    [InlineData(TenantRoles.Member)]
    [InlineData(TenantRoles.Admin)]
    public async Task NonOwnerCannotChangeMemberRoles(string callerRole)
    {
        var seed = await _factory.SeedTenantAsync(callerRole);
        using var client = CreateClient(seed.UserId);

        var response = await client.PatchAsJsonAsync(
            $"/api/tenants/{seed.TenantId}/members/{seed.OtherUserId}/role",
            new { role = TenantRoles.Admin });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(TenantRoles.Member, await ReadMembershipRoleAsync(seed.TenantId, seed.OtherUserId));
    }

    [Fact]
    public async Task NonMemberCannotChangeMemberRole()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Owner, includeCurrentUserMembership: false);
        using var client = CreateClient(seed.UserId);

        var response = await client.PatchAsJsonAsync(
            $"/api/tenants/{seed.TenantId}/members/{seed.OtherUserId}/role",
            new { role = TenantRoles.Admin });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OwnerCannotChangeOwnRole()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Owner);
        using var client = CreateClient(seed.UserId);

        var response = await client.PatchAsJsonAsync(
            $"/api/tenants/{seed.TenantId}/members/{seed.UserId}/role",
            new { role = TenantRoles.Member });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(TenantRoles.Owner, await ReadMembershipRoleAsync(seed.TenantId, seed.UserId));
    }

    [Fact]
    public async Task OwnerCannotChangeAnotherOwnersRole()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Owner);
        await SetMembershipRoleAsync(seed.TenantId, seed.OtherUserId, TenantRoles.Owner);
        using var client = CreateClient(seed.UserId);

        var response = await client.PatchAsJsonAsync(
            $"/api/tenants/{seed.TenantId}/members/{seed.OtherUserId}/role",
            new { role = TenantRoles.Member });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(TenantRoles.Owner, await ReadMembershipRoleAsync(seed.TenantId, seed.OtherUserId));
    }

    [Fact]
    public async Task OwnerCannotAssignOwnerRole()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Owner);
        using var client = CreateClient(seed.UserId);

        var response = await client.PatchAsJsonAsync(
            $"/api/tenants/{seed.TenantId}/members/{seed.OtherUserId}/role",
            new { role = TenantRoles.Owner });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(TenantRoles.Member, await ReadMembershipRoleAsync(seed.TenantId, seed.OtherUserId));
    }

    [Fact]
    public async Task InvalidMemberRoleReturnsBadRequest()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Owner);
        using var client = CreateClient(seed.UserId);

        var response = await client.PatchAsJsonAsync(
            $"/api/tenants/{seed.TenantId}/members/{seed.OtherUserId}/role",
            new { role = "NotARealRole" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MissingTargetMembershipReturnsNotFoundWhenChangingRole()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Owner);
        using var client = CreateClient(seed.UserId);

        var response = await client.PatchAsJsonAsync(
            $"/api/tenants/{seed.TenantId}/members/{Guid.NewGuid()}/role",
            new { role = TenantRoles.Admin });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(TenantRoles.Member)]
    [InlineData(TenantRoles.Admin)]
    public async Task OwnerCanRemoveMemberAndAdmin(string targetRole)
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Owner);
        if (targetRole == TenantRoles.Admin)
        {
            await SetMembershipRoleAsync(seed.TenantId, seed.OtherUserId, TenantRoles.Admin);
        }

        using var client = CreateClient(seed.UserId);
        var response = await client.DeleteAsync(
            $"/api/tenants/{seed.TenantId}/members/{seed.OtherUserId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await ReadMembershipRoleOrNullAsync(seed.TenantId, seed.OtherUserId));
    }

    [Theory]
    [InlineData(TenantRoles.Member)]
    [InlineData(TenantRoles.Admin)]
    public async Task NonOwnerCannotRemoveMembers(string callerRole)
    {
        var seed = await _factory.SeedTenantAsync(callerRole);
        using var client = CreateClient(seed.UserId);

        var response = await client.DeleteAsync(
            $"/api/tenants/{seed.TenantId}/members/{seed.OtherUserId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotNull(await ReadMembershipRoleOrNullAsync(seed.TenantId, seed.OtherUserId));
    }

    [Fact]
    public async Task OwnerCannotRemoveThemselves()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Owner);
        using var client = CreateClient(seed.UserId);

        var response = await client.DeleteAsync(
            $"/api/tenants/{seed.TenantId}/members/{seed.UserId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(TenantRoles.Owner, await ReadMembershipRoleAsync(seed.TenantId, seed.UserId));
    }

    [Fact]
    public async Task OwnerCannotRemoveAnotherOwner()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Owner);
        await SetMembershipRoleAsync(seed.TenantId, seed.OtherUserId, TenantRoles.Owner);
        using var client = CreateClient(seed.UserId);

        var response = await client.DeleteAsync(
            $"/api/tenants/{seed.TenantId}/members/{seed.OtherUserId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(TenantRoles.Owner, await ReadMembershipRoleAsync(seed.TenantId, seed.OtherUserId));
    }

    [Fact]
    public async Task RemovingMemberPreservesUserAccount()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Owner);
        using var client = CreateClient(seed.UserId);

        var response = await client.DeleteAsync(
            $"/api/tenants/{seed.TenantId}/members/{seed.OtherUserId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await dbContext.Users.AnyAsync(user => user.Id == seed.OtherUserId));
    }

    [Fact]
    public async Task RemovingMemberPreservesTheirMembershipInOtherTenants()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Owner, createUnrelatedTenant: true);
        using var client = CreateClient(seed.UserId);

        var response = await client.DeleteAsync(
            $"/api/tenants/{seed.TenantId}/members/{seed.OtherUserId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            TenantRoles.Member,
            await ReadMembershipRoleAsync(seed.UnrelatedTenantId, seed.OtherUserId));
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await dbContext.Tenants.AnyAsync(tenant => tenant.Id == seed.TenantId));
    }

    [Fact]
    public async Task MemberManagementRequiresAuthentication()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Owner);
        using var client = _factory.CreateClient();

        var roleResponse = await client.PatchAsJsonAsync(
            $"/api/tenants/{seed.TenantId}/members/{seed.OtherUserId}/role",
            new { role = TenantRoles.Admin });
        var removalResponse = await client.DeleteAsync(
            $"/api/tenants/{seed.TenantId}/members/{seed.OtherUserId}");

        Assert.Equal(HttpStatusCode.Unauthorized, roleResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, removalResponse.StatusCode);
    }

    [Fact]
    public async Task MissingTargetMembershipReturnsNotFoundWhenRemovingMember()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Owner);
        using var client = CreateClient(seed.UserId);

        var response = await client.DeleteAsync(
            $"/api/tenants/{seed.TenantId}/members/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task SetMembershipRoleAsync(Guid tenantId, Guid userId, string role)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var membership = await dbContext.TenantMemberships
            .SingleAsync(entry => entry.TenantId == tenantId && entry.UserId == userId);
        membership.Role = role;
        await dbContext.SaveChangesAsync();
    }

    private async Task<string> ReadMembershipRoleAsync(Guid tenantId, Guid userId) =>
        await ReadMembershipRoleOrNullAsync(tenantId, userId)
        ?? throw new InvalidOperationException("Expected tenant membership was not found.");

    private async Task<string?> ReadMembershipRoleOrNullAsync(Guid tenantId, Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.TenantMemberships
            .Where(entry => entry.TenantId == tenantId && entry.UserId == userId)
            .Select(entry => entry.Role)
            .SingleOrDefaultAsync();
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
        if (createUnrelatedTenant)
        {
            var unrelatedTenant = await dbContext.Tenants
                .SingleAsync(entry => entry.Id == unrelatedTenantId);
            dbContext.TenantMemberships.Add(NewMembership(otherUser, unrelatedTenant, TenantRoles.Member));
            await dbContext.SaveChangesAsync();
        }

        return new SeededTenant(currentUser.Id, otherUser.Id, tenant.Id, unrelatedTenantId);
    }

    public async Task<SeededUser> SeedUserAsync(string email)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureCreatedAsync();

        var user = NewUser(email);
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        return new SeededUser(user.Id, email);
    }

    public async Task<Guid> SeedProjectAsync(
        Guid tenantId,
        Guid createdByUserId,
        string name = "Test project",
        string status = ProjectStatuses.Planning,
        DateTime? createdAtUtc = null)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.EnsureCreatedAsync();

        var createdAt = createdAtUtc ?? DateTime.UtcNow;
        var project = new Project
        {
            TenantId = tenantId,
            Name = name,
            Status = status,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = createdAt
        };
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync();
        return project.Id;
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

public sealed record SeededTenant(Guid UserId, Guid OtherUserId, Guid TenantId, Guid UnrelatedTenantId);
public sealed record SeededUser(Guid UserId, string Email);

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
