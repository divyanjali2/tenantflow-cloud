using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenantFlow.Api.Contracts;
using TenantFlow.Api.Data;
using TenantFlow.Api.Tests.Infrastructure;

namespace TenantFlow.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class ProjectTests(PostgresFixture postgres) : ApiTest(postgres)
{
    [Theory]
    [InlineData("Owner")]
    [InlineData("Admin")]
    public async Task WritersCanCreateReadAndUpdateProjectsWithoutChangingServerFields(string role)
    {
        var owner = await RegisterAccountAsync();
        var tenant = await CreateTenantAsync(owner);
        var actor = owner;
        if (role == "Admin") { actor = await RegisterAccountAsync(); await AddMemberAsync(owner, tenant.TenantId, actor, role); }
        var project = await CreateProjectAsync(actor, tenant.TenantId);
        project.TenantId.Should().Be(tenant.TenantId);
        project.CreatedByUserId.Should().Be(actor.UserId);
        var list = (await actor.Client.GetFromJsonAsync<ProjectResponse[]>($"/api/tenants/{tenant.TenantId}/projects"))!;
        list.Should().ContainSingle().Which.Id.Should().Be(project.Id);
        var url = $"/api/tenants/{tenant.TenantId}/projects/{project.Id}";
        (await actor.Client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
        var update = await actor.Client.PutAsJsonAsync(url, new
        {
            name = "Updated project", description = "Updated description", status = "Active",
            tenantId = Guid.NewGuid(), createdByUserId = Guid.NewGuid(), createdAtUtc = DateTime.MinValue
        });
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        var changed = (await update.Content.ReadFromJsonAsync<ProjectResponse>())!;
        changed.Name.Should().Be("Updated project");
        changed.Description.Should().Be("Updated description");
        changed.Status.Should().Be("Active");
        changed.TenantId.Should().Be(project.TenantId);
        changed.CreatedByUserId.Should().Be(project.CreatedByUserId);
        // Compare the persisted value, respecting PostgreSQL's microsecond precision.
        changed.CreatedAtUtc.Should().Be(list.Single().CreatedAtUtc);
        changed.UpdatedAtUtc.Should().BeOnOrAfter(project.UpdatedAtUtc);
    }

    [Fact]
    public async Task OwnerDeletionCascadesToTasksButPreservesTenantUserAndMembership()
    {
        var owner = await RegisterAccountAsync();
        var tenant = await CreateTenantAsync(owner);
        var project = await CreateProjectAsync(owner, tenant.TenantId);
        await CreateTaskAsync(owner, tenant.TenantId, project.Id);
        (await owner.Client.DeleteAsync($"/api/tenants/{tenant.TenantId}/projects/{project.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Projects.CountAsync()).Should().Be(0);
        (await db.ProjectTasks.CountAsync()).Should().Be(0);
        (await db.Tenants.CountAsync()).Should().Be(1);
        (await db.Users.CountAsync()).Should().Be(1);
        (await db.TenantMemberships.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Member")]
    public async Task OnlyOwnerCanDeleteProjects(string role)
    {
        var owner = await RegisterAccountAsync();
        var actor = await RegisterAccountAsync();
        var tenant = await CreateTenantAsync(owner);
        await AddMemberAsync(owner, tenant.TenantId, actor, role);
        var project = await CreateProjectAsync(owner, tenant.TenantId);
        var url = $"/api/tenants/{tenant.TenantId}/projects/{project.Id}";
        (await actor.Client.DeleteAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.Client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("active")]
    public async Task InvalidStatusesReturnValidationErrorsOnCreateAndUpdate(string status)
    {
        var owner = await RegisterAccountAsync();
        var tenant = await CreateTenantAsync(owner);
        var project = await CreateProjectAsync(owner, tenant.TenantId);
        var url = $"/api/tenants/{tenant.TenantId}/projects";
        (await owner.Client.PostAsJsonAsync(url, new { name = "Invalid", status })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await owner.Client.PutAsJsonAsync($"{url}/{project.Id}", new { name = "Invalid", status })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await owner.Client.GetFromJsonAsync<ProjectResponse>($"{url}/{project.Id}"))!.Status.Should().Be("Planning");
    }

    [Fact]
    public async Task MemberCanViewButCannotCreateOrEdit()
    {
        var owner = await RegisterAccountAsync();
        var member = await RegisterAccountAsync();
        var tenant = await CreateTenantAsync(owner);
        await AddMemberAsync(owner, tenant.TenantId, member, "Member");
        var project = await CreateProjectAsync(owner, tenant.TenantId);
        var url = $"/api/tenants/{tenant.TenantId}/projects";
        (await member.Client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await member.Client.PostAsJsonAsync(url, new { name = "Forbidden" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.Client.PutAsJsonAsync($"{url}/{project.Id}", new { name = "Forbidden", status = "Active" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ProjectListAndResourceIdsCannotCrossTenantBoundariesEvenForOwnerOfBoth()
    {
        var owner = await RegisterAccountAsync();
        var first = await CreateTenantAsync(owner);
        var second = await CreateTenantAsync(owner);
        await CreateProjectAsync(owner, first.TenantId, name: "First tenant");
        var foreign = await CreateProjectAsync(owner, second.TenantId, name: "Second tenant");
        var url = $"/api/tenants/{first.TenantId}/projects";
        (await owner.Client.GetFromJsonAsync<ProjectResponse[]>(url))!.Should().ContainSingle().Which.Name.Should().Be("First tenant");
        var wrongScope = $"{url}/{foreign.Id}";
        (await owner.Client.GetAsync(wrongScope)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.Client.PutAsJsonAsync(wrongScope, new { name = "Wrong tenant", status = "Active" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.Client.DeleteAsync(wrongScope)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
