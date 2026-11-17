using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using TenantFlow.Api.Authorization;
using TenantFlow.Api.Configuration;
using TenantFlow.Api.Contracts;
using TenantFlow.Api.Data;
using TenantFlow.Api.Models;
using TenantFlow.Api.Services;

namespace TenantFlow.Tests;

public sealed class DashboardSummaryTests(DashboardApiFactory factory) : IClassFixture<DashboardApiFactory>
{
    private static string Url(Guid tenantId) => $"/api/tenants/{tenantId}/dashboard/summary";

    [Theory]
    [InlineData(TenantRoles.Owner)]
    [InlineData(TenantRoles.Admin)]
    [InlineData(TenantRoles.Member)]
    public async Task ValidJwtMembersCanReadAnEmptyTenant(string role)
    {
        var seed = await factory.SeedTenantAsync(role);
        using var client = factory.AuthenticatedClient(seed.UserId);
        var response = await client.GetAsync(Url(seed.TenantId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(new DashboardSummaryResponse(0, 0, 0, 0, 0, 0, 0, 0, 2, 0),
            await response.Content.ReadFromJsonAsync<DashboardSummaryResponse>());
    }

    [Fact]
    public async Task CountsProjectAndTaskStatusesAndUtcOverdueDatesOnlyWithinTenant()
    {
        var seed = await factory.SeedTenantAsync(TenantRoles.Member, createUnrelatedTenant: true);
        var projectId = await factory.SeedProjectAsync(seed.TenantId, seed.UserId, status: ProjectStatuses.Active);
        foreach (var status in new[] { ProjectStatuses.Completed, ProjectStatuses.Planning, ProjectStatuses.OnHold, ProjectStatuses.Cancelled })
            await factory.SeedProjectAsync(seed.TenantId, seed.UserId, status: status);
        var foreignProject = await factory.SeedProjectAsync(seed.UnrelatedTenantId, seed.OtherUserId, status: ProjectStatuses.Active);
        var now = DashboardApiFactory.Now.UtcDateTime;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ProjectTasks.AddRange(
                NewTask(seed.TenantId, projectId, seed.UserId, ProjectTaskStatuses.ToDo, now.AddTicks(-1)),
                NewTask(seed.TenantId, projectId, seed.UserId, ProjectTaskStatuses.InProgress, now.AddHours(-1)),
                NewTask(seed.TenantId, projectId, seed.UserId, ProjectTaskStatuses.Completed, now.AddDays(-1)),
                NewTask(seed.TenantId, projectId, seed.UserId, ProjectTaskStatuses.ToDo, now),
                NewTask(seed.TenantId, projectId, seed.UserId, ProjectTaskStatuses.InProgress, now.AddHours(1)),
                NewTask(seed.TenantId, projectId, seed.UserId, ProjectTaskStatuses.ToDo, null),
                NewTask(seed.TenantId, projectId, seed.UserId, ProjectTaskStatuses.Completed, null),
                NewTask(seed.UnrelatedTenantId, foreignProject, seed.OtherUserId, ProjectTaskStatuses.ToDo, now.AddDays(-3)),
                NewTask(seed.UnrelatedTenantId, foreignProject, seed.OtherUserId, ProjectTaskStatuses.Completed, null));
            await db.SaveChangesAsync();
        }
        using var client = factory.AuthenticatedClient(seed.UserId);
        var summary = await client.GetFromJsonAsync<DashboardSummaryResponse>(Url(seed.TenantId));
        Assert.Equal(new DashboardSummaryResponse(5, 1, 1, 7, 3, 2, 2, 2, 2, 28.57m), summary);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Url(seed.UnrelatedTenantId))).StatusCode);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 1, 100)]
    [InlineData(3, 1, 33.33)]
    public async Task CompletionPercentageHandlesZeroFractionalAndCompleteWork(int total, int completed, decimal expected)
    {
        var seed = await factory.SeedTenantAsync(TenantRoles.Owner);
        var projectId = await factory.SeedProjectAsync(seed.TenantId, seed.UserId, status: ProjectStatuses.Active);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            for (var index = 0; index < total; index++)
                db.ProjectTasks.Add(NewTask(seed.TenantId, projectId, seed.UserId,
                    index < completed ? ProjectTaskStatuses.Completed : ProjectTaskStatuses.ToDo, null));
            await db.SaveChangesAsync();
        }
        using var client = factory.AuthenticatedClient(seed.UserId);
        var summary = await client.GetFromJsonAsync<DashboardSummaryResponse>(Url(seed.TenantId));
        Assert.NotNull(summary);
        Assert.Equal(expected, summary.TaskCompletionPercentage);
        Assert.Equal(total, summary.TotalTasks);
        Assert.Equal(1, summary.ActiveProjects);
    }

    [Fact]
    public async Task NonmembersMissingTenantsAndRemovedMembersReceiveNotFound()
    {
        var seed = await factory.SeedTenantAsync(TenantRoles.Owner, includeCurrentUserMembership: false);
        using var client = factory.AuthenticatedClient(seed.UserId);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Url(seed.TenantId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Url(Guid.NewGuid()))).StatusCode);
        var memberSeed = await factory.SeedTenantAsync(TenantRoles.Admin);
        using var memberClient = factory.AuthenticatedClient(memberSeed.UserId);
        Assert.Equal(HttpStatusCode.OK, (await memberClient.GetAsync(Url(memberSeed.TenantId))).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.TenantMemberships.Remove(await db.TenantMemberships.SingleAsync(
                membership => membership.TenantId == memberSeed.TenantId && membership.UserId == memberSeed.UserId));
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.GetAsync(Url(memberSeed.TenantId))).StatusCode);
    }

    [Fact]
    public async Task AnonymousExpiredAndWronglySignedTokensAreUnauthorized()
    {
        var seed = await factory.SeedTenantAsync(TenantRoles.Owner);
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        // The shared fixture's test header must not substitute for a JWT here.
        anonymous.DefaultRequestHeaders.Add(TestAuthenticationHandler.UserIdHeader, seed.UserId.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Url(seed.TenantId))).StatusCode);
        foreach (var expired in new[] { true, false })
        {
            var key = expired ? factory.Jwt.Key : Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            var jwt = new JwtSecurityToken(factory.Jwt.Issuer, factory.Jwt.Audience,
                [new Claim(JwtRegisteredClaimNames.Sub, seed.UserId.ToString())],
                DateTime.UtcNow.AddHours(-2), expired ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow.AddHours(1),
                new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256));
            anonymous.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(jwt));
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Url(seed.TenantId))).StatusCode);
        }
    }

    private static ProjectTask NewTask(Guid tenant, Guid project, Guid user, string status, DateTime? due) =>
        new() { TenantId = tenant, ProjectId = project, CreatedByUserId = user, Title = "Analytics test task", Status = status, DueDateUtc = due };
}

public sealed class DashboardApiFactory : TenantApiFactory
{
    public static readonly DateTimeOffset Now = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
    public JwtSettings Jwt { get; } = new(Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)), "Dashboard.Tests", "Dashboard.Tests");
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(new FrozenClock());
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultForbidScheme = JwtBearerDefaults.AuthenticationScheme;
            });
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters.ValidIssuer = Jwt.Issuer;
                options.TokenValidationParameters.ValidAudience = Jwt.Audience;
                options.TokenValidationParameters.IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Jwt.Key));
            });
        });
    }
    public HttpClient AuthenticatedClient(Guid userId)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var token = new JwtTokenService(Jwt).CreateAccessToken(new ApplicationUser { Id = userId, Email = "dashboard@example.test" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }
    private sealed class FrozenClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
