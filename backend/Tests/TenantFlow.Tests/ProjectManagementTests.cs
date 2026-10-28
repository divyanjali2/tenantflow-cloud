using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenantFlow.Api.Authorization;
using TenantFlow.Api.Contracts;
using TenantFlow.Api.Data;
using TenantFlow.Api.Models;

namespace TenantFlow.Tests;

public sealed class ProjectManagementTests : IClassFixture<TenantApiFactory>
{
    private readonly TenantApiFactory _factory;

    public ProjectManagementTests(TenantApiFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData(TenantRoles.Owner)]
    [InlineData(TenantRoles.Admin)]
    public async Task OwnerAndAdminCanCreateProject(string role)
    {
        var seed = await _factory.SeedTenantAsync(role);
        using var client = CreateClient(seed.UserId);

        var response = await client.PostAsJsonAsync(
            $"/api/tenants/{seed.TenantId}/projects",
            new
            {
                name = "Website Redesign",
                description = "Redesign the company website",
                status = ProjectStatuses.Planning
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var project = await response.Content.ReadFromJsonAsync<ProjectResponse>();
        Assert.NotNull(project);
        Assert.Equal(seed.TenantId, project.TenantId);
        Assert.Equal(seed.UserId, project.CreatedByUserId);
        Assert.Equal(ProjectStatuses.Planning, project.Status);
        Assert.Equal("Website Redesign", project.Name);
        Assert.Equal(DateTimeKind.Utc, project.CreatedAtUtc.UtcDateTime.Kind);
        Assert.Equal(DateTimeKind.Utc, project.UpdatedAtUtc.UtcDateTime.Kind);
        Assert.Equal(project.CreatedAtUtc, project.UpdatedAtUtc);
    }

    [Fact]
    public async Task CreateProjectDefaultsStatusToPlanning()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Owner);
        using var client = CreateClient(seed.UserId);

        var response = await client.PostAsJsonAsync(
            $"/api/tenants/{seed.TenantId}/projects",
            new { name = "Default status project" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var project = await response.Content.ReadFromJsonAsync<ProjectResponse>();
        Assert.NotNull(project);
        Assert.Equal(ProjectStatuses.Planning, project.Status);
    }

    [Fact]
    public async Task MemberCannotCreateProject()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Member);
        using var client = CreateClient(seed.UserId);

        var response = await client.PostAsJsonAsync(
            $"/api/tenants/{seed.TenantId}/projects",
            new { name = "Forbidden project" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task NonMemberCannotCreateProject()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Owner, includeCurrentUserMembership: false);
        using var client = CreateClient(seed.UserId);

        var response = await client.PostAsJsonAsync(
            $"/api/tenants/{seed.TenantId}/projects",
            new { name = "Inaccessible project" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TenantMemberCanListProjectsInCreationOrder()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Member);
        var olderProjectId = await _factory.SeedProjectAsync(
            seed.TenantId,
            seed.UserId,
            "Older project",
            createdAtUtc: DateTime.UtcNow.AddMinutes(-1));
        var newerProjectId = await _factory.SeedProjectAsync(
            seed.TenantId,
            seed.UserId,
            "Newer project");
        using var client = CreateClient(seed.UserId);

        var response = await client.GetAsync($"/api/tenants/{seed.TenantId}/projects");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var projects = await response.Content.ReadFromJsonAsync<ProjectResponse[]>();
        Assert.NotNull(projects);
        Assert.Equal(
            new[] { newerProjectId, olderProjectId },
            projects.Select(project => project.Id).ToArray());
    }

    [Fact]
    public async Task NonMemberCannotListProjects()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Owner, includeCurrentUserMembership: false);
        using var client = CreateClient(seed.UserId);

        var response = await client.GetAsync($"/api/tenants/{seed.TenantId}/projects");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TenantProjectListDoesNotExposeAnotherTenantsProjects()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Owner, createUnrelatedTenant: true);
        var otherTenantProjectId = await _factory.SeedProjectAsync(
            seed.UnrelatedTenantId,
            seed.OtherUserId,
            "Other tenant project");
        using var client = CreateClient(seed.UserId);

        var response = await client.GetAsync($"/api/tenants/{seed.TenantId}/projects");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var projects = await response.Content.ReadFromJsonAsync<ProjectResponse[]>();
        Assert.NotNull(projects);
        Assert.DoesNotContain(projects, project => project.Id == otherTenantProjectId);
    }

    [Fact]
    public async Task MemberCannotRetrieveProjectFromAnotherTenant()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Member, createUnrelatedTenant: true);
        var otherTenantProjectId = await _factory.SeedProjectAsync(
            seed.UnrelatedTenantId,
            seed.OtherUserId,
            "Other tenant project");
        using var client = CreateClient(seed.UserId);

        var response = await client.GetAsync(
            $"/api/tenants/{seed.TenantId}/projects/{otherTenantProjectId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TenantMemberCanRetrieveProjectInTheirTenant()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Member);
        var projectId = await _factory.SeedProjectAsync(seed.TenantId, seed.OtherUserId);
        using var client = CreateClient(seed.UserId);

        var response = await client.GetAsync(
            $"/api/tenants/{seed.TenantId}/projects/{projectId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var project = await response.Content.ReadFromJsonAsync<ProjectResponse>();
        Assert.NotNull(project);
        Assert.Equal(projectId, project.Id);
        Assert.Equal(seed.TenantId, project.TenantId);
    }

    [Theory]
    [InlineData(TenantRoles.Owner)]
    [InlineData(TenantRoles.Admin)]
    public async Task OwnerAndAdminCanUpdateProject(string role)
    {
        var seed = await _factory.SeedTenantAsync(role);
        var projectId = await _factory.SeedProjectAsync(seed.TenantId, seed.OtherUserId);
        using var client = CreateClient(seed.UserId);

        var response = await client.PutAsJsonAsync(
            $"/api/tenants/{seed.TenantId}/projects/{projectId}",
            new
            {
                name = "Website Redesign V2",
                description = "Updated description",
                status = ProjectStatuses.Active,
                tenantId = seed.UnrelatedTenantId,
                createdByUserId = seed.OtherUserId,
                createdAtUtc = DateTime.MinValue
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var project = await response.Content.ReadFromJsonAsync<ProjectResponse>();
        Assert.NotNull(project);
        Assert.Equal("Website Redesign V2", project.Name);
        Assert.Equal("Updated description", project.Description);
        Assert.Equal(ProjectStatuses.Active, project.Status);
        Assert.Equal(seed.TenantId, project.TenantId);
        Assert.Equal(seed.OtherUserId, project.CreatedByUserId);
        Assert.NotEqual(DateTime.MinValue, project.CreatedAtUtc.UtcDateTime);
        Assert.True(project.UpdatedAtUtc >= project.CreatedAtUtc);
    }

    [Fact]
    public async Task MemberCannotUpdateProject()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Member);
        var projectId = await _factory.SeedProjectAsync(seed.TenantId, seed.OtherUserId);
        using var client = CreateClient(seed.UserId);

        var response = await client.PutAsJsonAsync(
            $"/api/tenants/{seed.TenantId}/projects/{projectId}",
            new { name = "Unauthorized update", status = ProjectStatuses.Active });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task OwnerCanDeleteProjectWithoutDeletingTenantOrUsers()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Owner);
        var projectId = await _factory.SeedProjectAsync(seed.TenantId, seed.OtherUserId);
        using var client = CreateClient(seed.UserId);

        var response = await client.DeleteAsync(
            $"/api/tenants/{seed.TenantId}/projects/{projectId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await dbContext.Projects.AnyAsync(project => project.Id == projectId));
        Assert.True(await dbContext.Tenants.AnyAsync(tenant => tenant.Id == seed.TenantId));
        Assert.True(await dbContext.Users.AnyAsync(user => user.Id == seed.OtherUserId));
    }

    [Theory]
    [InlineData(TenantRoles.Admin)]
    [InlineData(TenantRoles.Member)]
    public async Task NonOwnerCannotDeleteProject(string role)
    {
        var seed = await _factory.SeedTenantAsync(role);
        var projectId = await _factory.SeedProjectAsync(seed.TenantId, seed.OtherUserId);
        using var client = CreateClient(seed.UserId);

        var response = await client.DeleteAsync(
            $"/api/tenants/{seed.TenantId}/projects/{projectId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(await ReadProjectExistsAsync(projectId));
    }

    [Fact]
    public async Task InvalidProjectStatusReturnsBadRequest()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Owner);
        using var client = CreateClient(seed.UserId);

        var response = await client.PostAsJsonAsync(
            $"/api/tenants/{seed.TenantId}/projects",
            new { name = "Invalid status", status = "Unknown" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EmptyProjectNameReturnsBadRequest()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Owner);
        using var client = CreateClient(seed.UserId);

        var response = await client.PostAsJsonAsync(
            $"/api/tenants/{seed.TenantId}/projects",
            new { name = " ", status = ProjectStatuses.Planning });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MissingProjectInTenantReturnsNotFoundForDetailsAndUpdate()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Owner);
        using var client = CreateClient(seed.UserId);
        var projectId = Guid.NewGuid();

        var getResponse = await client.GetAsync(
            $"/api/tenants/{seed.TenantId}/projects/{projectId}");
        var updateResponse = await client.PutAsJsonAsync(
            $"/api/tenants/{seed.TenantId}/projects/{projectId}",
            new { name = "Missing", status = ProjectStatuses.Active });

        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, updateResponse.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedProjectRequestsReturnUnauthorized()
    {
        var seed = await _factory.SeedTenantAsync(TenantRoles.Owner);
        using var client = _factory.CreateClient();
        var body = new { name = "Unauthenticated", status = ProjectStatuses.Planning };
        var projectId = Guid.NewGuid();

        var createResponse = await client.PostAsJsonAsync(
            $"/api/tenants/{seed.TenantId}/projects",
            body);
        var listResponse = await client.GetAsync($"/api/tenants/{seed.TenantId}/projects");
        var getResponse = await client.GetAsync(
            $"/api/tenants/{seed.TenantId}/projects/{projectId}");
        var updateResponse = await client.PutAsJsonAsync(
            $"/api/tenants/{seed.TenantId}/projects/{projectId}",
            body);
        var deleteResponse = await client.DeleteAsync(
            $"/api/tenants/{seed.TenantId}/projects/{projectId}");

        Assert.Equal(HttpStatusCode.Unauthorized, createResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, getResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, updateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, deleteResponse.StatusCode);
    }

    private HttpClient CreateClient(Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.UserIdHeader, userId.ToString());
        return client;
    }

    private async Task<bool> ReadProjectExistsAsync(Guid projectId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.Projects.AnyAsync(project => project.Id == projectId);
    }
}
