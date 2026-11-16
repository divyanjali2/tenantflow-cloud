using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenantFlow.Api.Authorization;
using TenantFlow.Api.Contracts;
using TenantFlow.Api.Data;
using TenantFlow.Api.Models;

namespace TenantFlow.Tests;

public sealed class ProjectTaskManagementTests(TenantApiFactory factory) : IClassFixture<TenantApiFactory>
{
    [Theory]
    [InlineData(TenantRoles.Owner)]
    [InlineData(TenantRoles.Admin)]
    public async Task WritersCanPerformCrudAndCannotOverrideServerFields(string role)
    {
        var seed = await factory.SeedTenantAsync(role);
        var projectId = await factory.SeedProjectAsync(seed.TenantId, seed.OtherUserId);
        using var client = Client(seed.UserId);
        var path = Path(seed.TenantId, projectId);
        var dueDate = new DateTimeOffset(2027, 1, 15, 9, 30, 0, TimeSpan.FromHours(5.5));
        var create = await client.PostAsJsonAsync(path, new
        {
            title = "  Prepare release  ", description = "Release checklist",
            assignedToUserId = seed.OtherUserId, dueDateUtc = dueDate,
            tenantId = Guid.NewGuid(), projectId = Guid.NewGuid(),
            createdByUserId = seed.OtherUserId, createdAtUtc = DateTime.MinValue
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var task = await create.Content.ReadFromJsonAsync<ProjectTaskResponse>();
        Assert.NotNull(task);
        Assert.NotEqual(Guid.Empty, task.Id);
        Assert.Equal(seed.TenantId, task.TenantId);
        Assert.Equal(projectId, task.ProjectId);
        Assert.Equal(seed.UserId, task.CreatedByUserId);
        Assert.Equal("Prepare release", task.Title);
        Assert.Equal("Release checklist", task.Description);
        Assert.Equal(ProjectTaskStatuses.ToDo, task.Status);
        Assert.Equal(ProjectTaskPriorities.Medium, task.Priority);
        Assert.Equal(seed.OtherUserId, task.AssignedToUserId);
        Assert.Equal(dueDate.UtcDateTime, task.DueDateUtc);
        Assert.Equal(DateTimeKind.Utc, task.DueDateUtc!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, task.CreatedAtUtc.Kind);
        Assert.Equal(task.CreatedAtUtc, task.UpdatedAtUtc);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(create.Headers.Location)).StatusCode);
        var list = await client.GetFromJsonAsync<ProjectTaskPageResponse>(path);
        Assert.Single(list!.Items, entry => entry.Id == task.Id);

        var update = await client.PutAsJsonAsync($"{path}/{task.Id}", new
        {
            title = "Ready", status = ProjectTaskStatuses.Completed, priority = ProjectTaskPriorities.High,
            tenantId = Guid.NewGuid(), projectId = Guid.NewGuid(),
            createdByUserId = seed.OtherUserId, createdAtUtc = DateTime.MinValue
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<ProjectTaskResponse>();
        Assert.NotNull(updated);
        Assert.Equal(task.Id, updated.Id);
        Assert.Equal(task.TenantId, updated.TenantId);
        Assert.Equal(task.ProjectId, updated.ProjectId);
        Assert.Equal(task.CreatedByUserId, updated.CreatedByUserId);
        Assert.Equal(task.CreatedAtUtc, updated.CreatedAtUtc);
        Assert.True(updated.UpdatedAtUtc >= task.UpdatedAtUtc);
        Assert.Equal("Ready", updated.Title);
        Assert.Equal(ProjectTaskStatuses.Completed, updated.Status);
        Assert.Equal(ProjectTaskPriorities.High, updated.Priority);
        Assert.Null(updated.Description);
        Assert.Null(updated.AssignedToUserId);
        Assert.Null(updated.DueDateUtc);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"{path}/{task.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{path}/{task.Id}")).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.ProjectTasks.AnyAsync(entry => entry.Id == task.Id));
        Assert.True(await db.Projects.AnyAsync(entry => entry.Id == projectId));
        Assert.True(await db.Tenants.AnyAsync(entry => entry.Id == seed.TenantId));
        Assert.True(await db.Users.AnyAsync(entry => entry.Id == seed.UserId));
        Assert.True(await db.TenantMemberships.AnyAsync(entry => entry.TenantId == seed.TenantId && entry.UserId == seed.OtherUserId));
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("ASSIGNEE")]
    [InlineData("STATUS")]
    public async Task MemberCannotWrite(string method)
    {
        var seed = await factory.SeedTenantAsync(TenantRoles.Member);
        var projectId = await factory.SeedProjectAsync(seed.TenantId, seed.OtherUserId);
        var taskId = await SeedTaskAsync(seed.TenantId, projectId, seed.OtherUserId);
        using var client = Client(seed.UserId);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendAsync(client, method, Path(seed.TenantId, projectId), taskId)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal("Existing task", (await db.ProjectTasks.SingleAsync(entry => entry.Id == taskId)).Title);
    }

    [Theory]
    [InlineData(TenantRoles.Owner)]
    [InlineData(TenantRoles.Admin)]
    [InlineData(TenantRoles.Member)]
    public async Task MembersCanReadOnlyTasksInRequestedProject(string role)
    {
        var seed = await factory.SeedTenantAsync(role, createUnrelatedTenant: true);
        var projectId = await factory.SeedProjectAsync(seed.TenantId, seed.OtherUserId);
        var siblingId = await factory.SeedProjectAsync(seed.TenantId, seed.OtherUserId);
        var otherProjectId = await factory.SeedProjectAsync(seed.UnrelatedTenantId, seed.OtherUserId);
        var taskId = await SeedTaskAsync(seed.TenantId, projectId, seed.OtherUserId);
        await SeedTaskAsync(seed.TenantId, siblingId, seed.OtherUserId);
        await SeedTaskAsync(seed.UnrelatedTenantId, otherProjectId, seed.OtherUserId);
        using var client = Client(seed.UserId);
        var tasks = await client.GetFromJsonAsync<ProjectTaskPageResponse>(Path(seed.TenantId, projectId));
        Assert.Equal(taskId, Assert.Single(tasks!.Items).Id);
        Assert.Equal(HttpStatusCode.OK,
            (await client.GetAsync($"{Path(seed.TenantId, projectId)}/{taskId}")).StatusCode);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("LIST")]
    [InlineData("GET")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("ASSIGNEE")]
    [InlineData("STATUS")]
    public async Task AllEndpointsRequireAuthenticationAndMembership(string method)
    {
        var seed = await factory.SeedTenantAsync(TenantRoles.Owner, includeCurrentUserMembership: false);
        var projectId = await factory.SeedProjectAsync(seed.TenantId, seed.OtherUserId);
        var taskId = await SeedTaskAsync(seed.TenantId, projectId, seed.OtherUserId);
        using var anonymous = factory.CreateClient();
        using var nonMember = Client(seed.UserId);
        var path = Path(seed.TenantId, projectId);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(anonymous, method, path, taskId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(nonMember, method, path, taskId)).StatusCode);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("LIST")]
    [InlineData("GET")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("ASSIGNEE")]
    [InlineData("STATUS")]
    public async Task MissingOrCrossTenantProjectIsNotFound(string method)
    {
        var seed = await factory.SeedTenantAsync(TenantRoles.Owner, createUnrelatedTenant: true);
        var foreignProjectId = await factory.SeedProjectAsync(seed.UnrelatedTenantId, seed.OtherUserId);
        var taskId = await SeedTaskAsync(seed.UnrelatedTenantId, foreignProjectId, seed.OtherUserId);
        using var client = Client(seed.UserId);
        foreach (var projectId in new[] { Guid.NewGuid(), foreignProjectId })
            Assert.Equal(HttpStatusCode.NotFound,
                (await SendAsync(client, method, Path(seed.TenantId, projectId), taskId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await SendAsync(client, method, Path(seed.UnrelatedTenantId, foreignProjectId), taskId)).StatusCode);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("ASSIGNEE")]
    [InlineData("STATUS")]
    public async Task MissingCrossProjectAndCrossTenantTaskIdsAreNotFound(string method)
    {
        var seed = await factory.SeedTenantAsync(TenantRoles.Owner, createUnrelatedTenant: true);
        var projectId = await factory.SeedProjectAsync(seed.TenantId, seed.OtherUserId);
        var siblingId = await factory.SeedProjectAsync(seed.TenantId, seed.OtherUserId);
        var foreignProjectId = await factory.SeedProjectAsync(seed.UnrelatedTenantId, seed.OtherUserId);
        var siblingTaskId = await SeedTaskAsync(seed.TenantId, siblingId, seed.OtherUserId);
        var foreignTaskId = await SeedTaskAsync(seed.UnrelatedTenantId, foreignProjectId, seed.OtherUserId);
        using var client = Client(seed.UserId);
        foreach (var taskId in new[] { Guid.NewGuid(), siblingTaskId, foreignTaskId })
            Assert.Equal(HttpStatusCode.NotFound,
                (await SendAsync(client, method, Path(seed.TenantId, projectId), taskId)).StatusCode);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    public async Task InvalidValuesReturnValidationProblemAndLeaveTaskUnchanged(string method)
    {
        var seed = await factory.SeedTenantAsync(TenantRoles.Owner, createUnrelatedTenant: true);
        var projectId = await factory.SeedProjectAsync(seed.TenantId, seed.OtherUserId);
        var taskId = await SeedTaskAsync(seed.TenantId, projectId, seed.OtherUserId);
        var outsider = await factory.SeedUserAsync($"outsider-{Guid.NewGuid():N}@example.com");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.TenantMemberships.Add(new TenantMembership
            {
                Id = Guid.NewGuid(), TenantId = seed.UnrelatedTenantId,
                UserId = outsider.UserId, Role = TenantRoles.Member
            });
            await db.SaveChangesAsync();
        }
        using var client = Client(seed.UserId);
        var bodies = new object[]
        {
            new { title = "" }, new { title = " \t " }, new { title = new string('x', 201) },
            new { description = "Missing title" }, new { title = (string?)null },
            new { title = "Valid", status = "Unknown" }, new { title = "Valid", status = "todo" },
            new { title = "Valid", status = (string?)null },
            new { title = "Valid", priority = "Urgent" }, new { title = "Valid", priority = "high" },
            new { title = "Valid", priority = (string?)null },
            new { title = "Valid", assignedToUserId = Guid.NewGuid() },
            new { title = "Valid", assignedToUserId = outsider.UserId },
            new { title = "Valid", dueDateUtc = "invalid date" }
        };
        var path = Path(seed.TenantId, projectId);
        foreach (var body in bodies)
        {
            var response = method == "POST"
                ? await client.PostAsJsonAsync(path, body)
                : await client.PutAsJsonAsync($"{path}/{taskId}", body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }
        using var checkScope = factory.Services.CreateScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tasks = await checkDb.ProjectTasks.Where(entry => entry.TenantId == seed.TenantId && entry.ProjectId == projectId).ToListAsync();
        Assert.Equal("Existing task", Assert.Single(tasks).Title);
    }

    [Fact]
    public async Task RemovedMembershipCannotBeAssignedAndDoesNotDeleteExistingTasks()
    {
        var seed = await factory.SeedTenantAsync(TenantRoles.Owner);
        var projectId = await factory.SeedProjectAsync(seed.TenantId, seed.UserId);
        using var client = Client(seed.UserId);
        var path = Path(seed.TenantId, projectId);
        var create = await client.PostAsJsonAsync(path, new { title = "Keep history", assignedToUserId = seed.OtherUserId });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var task = (await create.Content.ReadFromJsonAsync<ProjectTaskResponse>())!;
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/tenants/{seed.TenantId}/members/{seed.OtherUserId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{path}/{task.Id}")).StatusCode);
        var body = new { title = "Cannot reassign", assignedToUserId = seed.OtherUserId };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path, body)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"{path}/{task.Id}", body)).StatusCode);
        using var removedMember = Client(seed.OtherUserId);
        Assert.Equal(HttpStatusCode.NotFound, (await removedMember.GetAsync(path)).StatusCode);
    }

    [Theory]
    [InlineData(ProjectTaskStatuses.ToDo, ProjectTaskPriorities.Low)]
    [InlineData(ProjectTaskStatuses.InProgress, ProjectTaskPriorities.Medium)]
    [InlineData(ProjectTaskStatuses.Completed, ProjectTaskPriorities.High)]
    public async Task SupportedStatusesPrioritiesAndMaximumTitleAreAccepted(string status, string priority)
    {
        var seed = await factory.SeedTenantAsync(TenantRoles.Owner);
        var projectId = await factory.SeedProjectAsync(seed.TenantId, seed.UserId);
        using var client = Client(seed.UserId);
        var response = await client.PostAsJsonAsync(Path(seed.TenantId, projectId),
            new { title = new string('x', 200), status, priority });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var task = (await response.Content.ReadFromJsonAsync<ProjectTaskResponse>())!;
        Assert.Equal(status, task.Status);
        Assert.Equal(priority, task.Priority);
    }

    [Fact]
    public void PostgresModelHasRequiredRelationshipsIndexesAndCascadeBehavior()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql().Options);
        var entity = db.Model.FindEntityType(typeof(ProjectTask))!;
        Assert.Equal(nameof(ProjectTask.Id), Assert.Single(entity.FindPrimaryKey()!.Properties).Name);
        foreach (var name in new[] { nameof(ProjectTask.TenantId), nameof(ProjectTask.ProjectId), nameof(ProjectTask.AssignedToUserId) })
            Assert.Contains(entity.GetIndexes(), index => index.Properties.Select(property => property.Name).SequenceEqual(new[] { name }));
        foreach (var fk in entity.GetForeignKeys())
        {
            var name = Assert.Single(fk.Properties).Name;
            var expected = name is nameof(ProjectTask.ProjectId) or nameof(ProjectTask.TenantId)
                ? DeleteBehavior.Cascade : DeleteBehavior.Restrict;
            Assert.Equal(expected, fk.DeleteBehavior);
        }
        Assert.Equal(4, entity.GetForeignKeys().Count());
        Assert.Equal(200, entity.FindProperty(nameof(ProjectTask.Title))!.GetMaxLength());
        Assert.False(entity.FindProperty(nameof(ProjectTask.Title))!.IsNullable);
        Assert.Equal(ProjectTaskStatuses.ToDo, entity.FindProperty(nameof(ProjectTask.Status))!.GetDefaultValue());
        Assert.Equal(ProjectTaskPriorities.Medium, entity.FindProperty(nameof(ProjectTask.Priority))!.GetDefaultValue());
        Assert.Equal("timestamp with time zone", entity.FindProperty(nameof(ProjectTask.DueDateUtc))!.GetColumnType());
    }

    private HttpClient Client(Guid userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.UserIdHeader, userId.ToString());
        return client;
    }

    private static string Path(Guid tenantId, Guid projectId) => $"/api/tenants/{tenantId}/projects/{projectId}/tasks";

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path, Guid taskId) => method switch
    {
        "POST" => client.PostAsJsonAsync(path, new { title = "Changed task" }),
        "PUT" => client.PutAsJsonAsync($"{path}/{taskId}", new { title = "Changed task" }),
        "DELETE" => client.DeleteAsync($"{path}/{taskId}"),
        "ASSIGNEE" => client.PatchAsJsonAsync($"{path}/{taskId}/assignee", new { assignedToUserId = (Guid?)null }),
        "STATUS" => client.PatchAsJsonAsync($"{path}/{taskId}/status", new { status = ProjectTaskStatuses.InProgress }),
        "LIST" => client.GetAsync(path),
        _ => client.GetAsync($"{path}/{taskId}")
    };

    private async Task<Guid> SeedTaskAsync(Guid tenantId, Guid projectId, Guid creatorId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var task = new ProjectTask { TenantId = tenantId, ProjectId = projectId, CreatedByUserId = creatorId, Title = "Existing task" };
        db.ProjectTasks.Add(task);
        await db.SaveChangesAsync();
        return task.Id;
    }
}
