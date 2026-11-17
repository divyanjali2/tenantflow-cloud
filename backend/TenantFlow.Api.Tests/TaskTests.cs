using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenantFlow.Api.Contracts;
using TenantFlow.Api.Data;
using TenantFlow.Api.Tests.Infrastructure;

namespace TenantFlow.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class TaskTests(PostgresFixture postgres) : ApiTest(postgres)
{
    [Theory]
    [InlineData("Owner")]
    [InlineData("Admin")]
    public async Task WritersCanCreateUpdateAndDeleteTasks(string role)
    {
        var owner = await RegisterAccountAsync();
        var tenant = await CreateTenantAsync(owner);
        var project = await CreateProjectAsync(owner, tenant.TenantId);
        var actor = owner;
        if (role == "Admin") { actor = await RegisterAccountAsync(); await AddMemberAsync(owner, tenant.TenantId, actor, role); }
        var task = await CreateTaskAsync(actor, tenant.TenantId, project.Id);
        task.Status.Should().Be("ToDo");
        task.Priority.Should().Be("Medium");
        var url = $"{TasksPath(tenant.TenantId, project.Id)}/{task.Id}";
        var due = Factory.Clock.GetUtcNow().AddDays(2);
        var updated = await actor.Client.PutAsJsonAsync(url, new
        {
            title = "Updated task", description = "Preserve actual records", status = "InProgress", priority = "High",
            assignedToUserId = actor.UserId, dueDateUtc = due, tenantId = Guid.NewGuid(), projectId = Guid.NewGuid(), createdByUserId = Guid.NewGuid()
        });
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await updated.Content.ReadFromJsonAsync<ProjectTaskResponse>())!;
        result.Title.Should().Be("Updated task");
        result.Status.Should().Be("InProgress");
        result.Priority.Should().Be("High");
        result.AssignedToUserId.Should().Be(actor.UserId);
        result.DueDateUtc.Should().Be(due.UtcDateTime);
        result.TenantId.Should().Be(tenant.TenantId);
        result.ProjectId.Should().Be(project.Id);
        result.CreatedByUserId.Should().Be(task.CreatedByUserId);
        (await actor.Client.DeleteAsync(url)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await actor.Client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await using var scope = Factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Projects.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task AssignmentRequiresCurrentSameTenantMembershipAndSupportsUnassignment()
    {
        var owner = await RegisterAccountAsync();
        var member = await RegisterAccountAsync();
        var outside = await RegisterAccountAsync();
        var tenant = await CreateTenantAsync(owner);
        var other = await CreateTenantAsync(outside);
        await AddMemberAsync(owner, tenant.TenantId, member, "Member");
        var project = await CreateProjectAsync(owner, tenant.TenantId);
        var task = await CreateTaskAsync(owner, tenant.TenantId, project.Id);
        var url = $"{TasksPath(tenant.TenantId, project.Id)}/{task.Id}/assignee";
        var assign = await owner.Client.PatchAsJsonAsync(url, new { assignedToUserId = member.UserId });
        assign.StatusCode.Should().Be(HttpStatusCode.OK);
        (await assign.Content.ReadFromJsonAsync<ProjectTaskResponse>())!.AssignedToUserId.Should().Be(member.UserId);
        foreach (var id in new[] { outside.UserId, Guid.NewGuid() })
            (await owner.Client.PatchAsJsonAsync(url, new { assignedToUserId = id })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var unassign = await owner.Client.PatchAsJsonAsync(url, new { assignedToUserId = (Guid?)null });
        unassign.StatusCode.Should().Be(HttpStatusCode.OK);
        (await unassign.Content.ReadFromJsonAsync<ProjectTaskResponse>())!.AssignedToUserId.Should().BeNull();
        (await owner.Client.DeleteAsync($"/api/tenants/{tenant.TenantId}/members/{member.UserId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await owner.Client.PatchAsJsonAsync(url, new { assignedToUserId = member.UserId })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MemberCanUpdateOnlyOwnAssignedStatusAndCannotWriteOtherTaskFields()
    {
        var owner = await RegisterAccountAsync();
        var member = await RegisterAccountAsync();
        var tenant = await CreateTenantAsync(owner);
        await AddMemberAsync(owner, tenant.TenantId, member, "Member");
        var project = await CreateProjectAsync(owner, tenant.TenantId);
        var own = await CreateTaskAsync(owner, tenant.TenantId, project.Id, assignee: member.UserId);
        var unassigned = await CreateTaskAsync(owner, tenant.TenantId, project.Id);
        var path = TasksPath(tenant.TenantId, project.Id);
        (await member.Client.PatchAsJsonAsync($"{path}/{own.Id}/status", new { status = "Completed" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await member.Client.PatchAsJsonAsync($"{path}/{unassigned.Id}/status", new { status = "Completed" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.Client.PatchAsJsonAsync($"{path}/{own.Id}/assignee", new { assignedToUserId = owner.UserId })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.Client.PostAsJsonAsync(path, new { title = "Forbidden" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.Client.PutAsJsonAsync($"{path}/{own.Id}", new { title = "Forbidden" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.Client.DeleteAsync($"{path}/{own.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.Client.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("priority")]
    [InlineData("title")]
    public async Task InvalidTaskValuesDoNotPersist(string field)
    {
        var owner = await RegisterAccountAsync();
        var tenant = await CreateTenantAsync(owner);
        var project = await CreateProjectAsync(owner, tenant.TenantId);
        var body = new Dictionary<string, object> { ["title"] = "Valid title", ["status"] = "ToDo", ["priority"] = "Medium" };
        body[field] = field == "title" ? "   " : "Unsupported";
        (await owner.Client.PostAsJsonAsync(TasksPath(tenant.TenantId, project.Id), body)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await using var scope = Factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().ProjectTasks.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task FiltersAndPaginationExecuteAgainstPostgresWithLiteralCaseInsensitiveSearch()
    {
        var owner = await RegisterAccountAsync();
        var tenant = await CreateTenantAsync(owner);
        var project = await CreateProjectAsync(owner, tenant.TenantId);
        for (var index = 0; index < 6; index++)
            await CreateTaskAsync(owner, tenant.TenantId, project.Id, title: $"Alpha %_ task {index}", status: "InProgress", priority: "High", assignee: owner.UserId);
        await CreateTaskAsync(owner, tenant.TenantId, project.Id, title: "Unrelated task", status: "Completed", priority: "Low");
        var url = TasksPath(tenant.TenantId, project.Id);
        var first = (await owner.Client.GetFromJsonAsync<ProjectTaskPageResponse>($"{url}?status=InProgress&priority=High&assignedToUserId={owner.UserId}&search=ALPHA%20%25_&page=1&pageSize=4"))!;
        var second = (await owner.Client.GetFromJsonAsync<ProjectTaskPageResponse>($"{url}?status=InProgress&priority=High&assignedToUserId={owner.UserId}&search=ALPHA%20%25_&page=2&pageSize=4"))!;
        first.TotalCount.Should().Be(6);
        first.TotalPages.Should().Be(2);
        first.Items.Should().HaveCount(4);
        second.Items.Should().HaveCount(2);
        first.Items.Select(task => task.Id).Should().NotIntersectWith(second.Items.Select(task => task.Id));
        first.Items.Should().OnlyContain(task => task.Status == "InProgress" && task.Priority == "High" && task.AssignedToUserId == owner.UserId);
        var again = (await owner.Client.GetFromJsonAsync<ProjectTaskPageResponse>($"{url}?status=InProgress&priority=High&pageSize=4"))!;
        again.Items.Select(task => task.Id).Should().Equal(first.Items.Select(task => task.Id));
        var empty = (await owner.Client.GetFromJsonAsync<ProjectTaskPageResponse>($"{url}?page=2147483647&pageSize=100"))!;
        empty.Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("status=Unknown")]
    public async Task InvalidTaskQueriesReturnBadRequest(string query)
    {
        var owner = await RegisterAccountAsync();
        var tenant = await CreateTenantAsync(owner);
        var project = await CreateProjectAsync(owner, tenant.TenantId);
        (await owner.Client.GetAsync($"{TasksPath(tenant.TenantId, project.Id)}?{query}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TasksCannotBeAccessedThroughAnotherTenantOrProject()
    {
        var owner = await RegisterAccountAsync();
        var first = await CreateTenantAsync(owner);
        var second = await CreateTenantAsync(owner);
        var project = await CreateProjectAsync(owner, first.TenantId);
        var sibling = await CreateProjectAsync(owner, first.TenantId);
        var foreign = await CreateProjectAsync(owner, second.TenantId);
        var task = await CreateTaskAsync(owner, first.TenantId, project.Id);
        foreach (var path in new[] { TasksPath(first.TenantId, sibling.Id), TasksPath(second.TenantId, foreign.Id), TasksPath(second.TenantId, project.Id) })
        {
            (await owner.Client.GetAsync($"{path}/{task.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await owner.Client.PutAsJsonAsync($"{path}/{task.Id}", new { title = "Wrong scope" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await owner.Client.DeleteAsync($"{path}/{task.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await owner.Client.PatchAsJsonAsync($"{path}/{task.Id}/status", new { status = "Completed" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await owner.Client.PatchAsJsonAsync($"{path}/{task.Id}/assignee", new { assignedToUserId = owner.UserId })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }
}
