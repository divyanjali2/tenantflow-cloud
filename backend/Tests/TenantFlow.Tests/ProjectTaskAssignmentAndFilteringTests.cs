using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenantFlow.Api.Authorization;
using TenantFlow.Api.Contracts;
using TenantFlow.Api.Data;
using TenantFlow.Api.Models;

namespace TenantFlow.Tests;

public sealed class ProjectTaskAssignmentAndFilteringTests(TenantApiFactory factory) : IClassFixture<TenantApiFactory>
{
    [Theory]
    [InlineData(TenantRoles.Owner)]
    [InlineData(TenantRoles.Admin)]
    public async Task WritersCanAssignReassignAndUnassignWithoutChangingOtherFields(string role)
    {
        var (seed, projectId, tasks) = await SetupAsync(role);
        using var client = Client(seed.UserId);
        var path = $"{Path(seed.TenantId, projectId)}/{tasks[0].Id}";
        var original = (await client.GetFromJsonAsync<ProjectTaskResponse>(path))!;
        foreach (var assignee in new Guid?[] { seed.OtherUserId, seed.UserId, null })
        {
            var response = await client.PatchAsJsonAsync($"{path}/assignee", new
            {
                assignedToUserId = assignee, tenantId = Guid.NewGuid(), projectId = Guid.NewGuid(),
                createdByUserId = seed.OtherUserId, title = "Must not change", status = "Completed"
            });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var updated = (await response.Content.ReadFromJsonAsync<ProjectTaskResponse>())!;
            Assert.True(updated.UpdatedAtUtc > original.UpdatedAtUtc);
            Assert.Equal(original with { AssignedToUserId = assignee, UpdatedAtUtc = updated.UpdatedAtUtc }, updated);
            Assert.Equal(updated, await client.GetFromJsonAsync<ProjectTaskResponse>(path));
        }
    }

    [Fact]
    public async Task InvalidAssigneesAndMalformedAssignmentBodiesReturnBadRequestWithoutChanges()
    {
        var (seed, projectId, tasks) = await SetupAsync(TenantRoles.Owner);
        var outsider = await factory.SeedTenantAsync(TenantRoles.Member);
        var registeredNonmember = await factory.SeedUserAsync($"nonmember-{Guid.NewGuid():N}@example.com");
        using var client = Client(seed.UserId);
        var path = $"{Path(seed.TenantId, projectId)}/{tasks[0].Id}";
        var original = await client.GetFromJsonAsync<ProjectTaskResponse>(path);
        var bodies = new object[]
        {
            new { assignedToUserId = outsider.UserId },
            new { assignedToUserId = registeredNonmember.UserId },
            new { assignedToUserId = Guid.NewGuid() }, new { assignedToUserId = Guid.Empty },
            new { assignedToUserId = "not-a-guid" }, new { userId = seed.OtherUserId }
        };
        foreach (var body in bodies)
        {
            var response = await client.PatchAsJsonAsync($"{path}/assignee", body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }
        Assert.Equal(original, await client.GetFromJsonAsync<ProjectTaskResponse>(path));

        // A removed membership cannot be used for a new assignment either.
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/api/tenants/{seed.TenantId}/members/{seed.OtherUserId}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PatchAsJsonAsync($"{path}/assignee", new { assignedToUserId = seed.OtherUserId })).StatusCode);
    }

    [Theory]
    [InlineData(TenantRoles.Owner)]
    [InlineData(TenantRoles.Admin)]
    public async Task WritersCanUpdateAnyTasksStatus(string role)
    {
        var (seed, projectId, tasks) = await SetupAsync(role);
        using var client = Client(seed.UserId);
        foreach (var task in tasks)
        {
            var path = $"{Path(seed.TenantId, projectId)}/{task.Id}";
            var original = (await client.GetFromJsonAsync<ProjectTaskResponse>(path))!;
            var response = await client.PatchAsJsonAsync($"{path}/status", new { status = ProjectTaskStatuses.Completed });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var updated = (await response.Content.ReadFromJsonAsync<ProjectTaskResponse>())!;
            Assert.True(updated.UpdatedAtUtc > original.UpdatedAtUtc);
            Assert.Equal(original with { Status = ProjectTaskStatuses.Completed, UpdatedAtUtc = updated.UpdatedAtUtc }, updated);
        }
    }

    [Fact]
    public async Task MemberCanUpdateOnlyOwnStatusAndCannotSpoofAssignmentOrChangeOtherFields()
    {
        var (seed, projectId, tasks) = await SetupAsync(TenantRoles.Member);
        using var client = Client(seed.UserId);
        var path = Path(seed.TenantId, projectId);
        foreach (var task in tasks)
        {
            var original = (await client.GetFromJsonAsync<ProjectTaskResponse>($"{path}/{task.Id}"))!;
            var response = await client.PatchAsJsonAsync($"{path}/{task.Id}/status", new
            {
                status = ProjectTaskStatuses.InProgress, assignedToUserId = seed.UserId,
                userId = seed.OtherUserId, tenantId = Guid.NewGuid(), title = "Must not change"
            });
            if (task.AssignedToUserId == seed.UserId)
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var updated = (await response.Content.ReadFromJsonAsync<ProjectTaskResponse>())!;
                Assert.True(updated.UpdatedAtUtc > original.UpdatedAtUtc);
                Assert.Equal(original with { Status = ProjectTaskStatuses.InProgress, UpdatedAtUtc = updated.UpdatedAtUtc }, updated);
                Assert.Equal(HttpStatusCode.Forbidden,
                    (await client.PatchAsJsonAsync($"{path}/{task.Id}/assignee", new { assignedToUserId = seed.UserId })).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden,
                    (await client.PutAsJsonAsync($"{path}/{task.Id}", new { title = "Cannot edit" })).StatusCode);
            }
            else
            {
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.Equal(original, await client.GetFromJsonAsync<ProjectTaskResponse>($"{path}/{task.Id}"));
            }
        }
    }

    [Theory]
    [InlineData(TenantRoles.Owner)]
    [InlineData(TenantRoles.Admin)]
    [InlineData(TenantRoles.Member)]
    public async Task InvalidStatusBodiesReturnBadRequestAndPreserveTask(string role)
    {
        var (seed, projectId, tasks) = await SetupAsync(role);
        using var client = Client(seed.UserId);
        var task = tasks.Single(entry => entry.AssignedToUserId == seed.UserId);
        var path = $"{Path(seed.TenantId, projectId)}/{task.Id}";
        var original = await client.GetFromJsonAsync<ProjectTaskResponse>(path);
        foreach (var body in new object[]
        {
            new { status = "Unknown" }, new { status = "inprogress" }, new { status = "" },
            new { status = " " }, new { status = (string?)null }, new { title = "Missing status" }, new { status = 1 }
        })
        {
            var response = await client.PatchAsJsonAsync($"{path}/status", body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }
        Assert.Equal(original, await client.GetFromJsonAsync<ProjectTaskResponse>(path));
    }

    [Fact]
    public async Task ReassignmentAndMembershipRemovalImmediatelyRevokeMemberStatusPermission()
    {
        var (seed, projectId, tasks) = await SetupAsync(TenantRoles.Owner);
        var task = tasks.Single(entry => entry.AssignedToUserId == seed.OtherUserId);
        var path = $"{Path(seed.TenantId, projectId)}/{task.Id}";
        using var owner = Client(seed.UserId);
        using var member = Client(seed.OtherUserId);
        var statusBody = new { status = ProjectTaskStatuses.InProgress };
        Assert.Equal(HttpStatusCode.OK, (await member.PatchAsJsonAsync($"{path}/status", statusBody)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await owner.PatchAsJsonAsync($"{path}/assignee", new { assignedToUserId = seed.UserId })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PatchAsJsonAsync($"{path}/status", statusBody)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await owner.PatchAsJsonAsync($"{path}/assignee", new { assignedToUserId = seed.OtherUserId })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await owner.DeleteAsync($"/api/tenants/{seed.TenantId}/members/{seed.OtherUserId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.PatchAsJsonAsync($"{path}/status", statusBody)).StatusCode);
    }

    [Fact]
    public async Task FiltersCombineWithinTenantAndProjectAndSearchOnlyTitleAsLiteralText()
    {
        var (seed, projectId, tasks) = await SetupAsync(TenantRoles.Member);
        var path = Path(seed.TenantId, projectId);
        using var client = Client(seed.UserId);
        var cases = new (string Query, Guid[] Ids)[]
        {
            ("status=ToDo", tasks.Where(task => task.Status == ProjectTaskStatuses.ToDo).Select(task => task.Id).ToArray()),
            ("priority=High", tasks.Where(task => task.Priority == ProjectTaskPriorities.High).Select(task => task.Id).ToArray()),
            ($"assignedToUserId={seed.UserId}", [tasks[1].Id]),
            ("search=ALPHA", [tasks[0].Id, tasks[1].Id]),
            ("search=%20aLpHa%20", [tasks[0].Id, tasks[1].Id]),
            ("search=%25_", [tasks[2].Id]),
            ("search=description-only", []),
            ($"status=InProgress&priority=High&assignedToUserId={seed.UserId}&search=ALPHA", [tasks[1].Id]),
            ("status=Completed&priority=Low", []),
            ($"assignedToUserId={Guid.NewGuid()}", []),
            ("search=%20%20", tasks.Select(task => task.Id).ToArray())
        };
        foreach (var (query, ids) in cases)
        {
            var page = (await client.GetFromJsonAsync<ProjectTaskPageResponse>($"{path}?{query}"))!;
            Assert.Equal(ids.Order(), page.Items.Select(task => task.Id).Order());
            Assert.Equal(ids.Length, page.TotalCount);
            Assert.Equal(ids.Length == 0 ? 0 : 1, page.TotalPages);
            Assert.Equal(1, page.Page);
            Assert.Equal(20, page.PageSize);
        }
    }

    [Fact]
    public async Task PaginationHasStableOrderingDefaultsCorrectTotalsAndEmptyOutOfRangePages()
    {
        var (seed, projectId, _) = await SetupAsync(TenantRoles.Member);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var time = DateTime.UtcNow.AddHours(-1);
            // Identical creation times force the Id tie-breaker across page boundaries.
            for (var i = 0; i < 22; i++)
                db.ProjectTasks.Add(new ProjectTask
                {
                    TenantId = seed.TenantId, ProjectId = projectId, CreatedByUserId = seed.UserId,
                    Title = $"Pagination {i}", CreatedAtUtc = time
                });
            await db.SaveChangesAsync();
        }
        using var client = Client(seed.UserId);
        var path = Path(seed.TenantId, projectId);
        var first = (await client.GetFromJsonAsync<ProjectTaskPageResponse>(path))!;
        var repeated = (await client.GetFromJsonAsync<ProjectTaskPageResponse>(path))!;
        var second = (await client.GetFromJsonAsync<ProjectTaskPageResponse>($"{path}?page=2"))!;
        Assert.Equal(1, first.Page);
        Assert.Equal(20, first.PageSize);
        Assert.Equal(25, first.TotalCount);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(20, first.Items.Count);
        Assert.Equal(5, second.Items.Count);
        Assert.Equal(2, second.Page);
        Assert.Equal(25, second.TotalCount);
        Assert.Equal(2, second.TotalPages);
        Assert.Equal(first.Items.Select(task => task.Id), repeated.Items.Select(task => task.Id));
        Assert.Empty(first.Items.Select(task => task.Id).Intersect(second.Items.Select(task => task.Id)));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var expected = await db.ProjectTasks.AsNoTracking()
                .Where(task => task.TenantId == seed.TenantId && task.ProjectId == projectId)
                .OrderByDescending(task => task.CreatedAtUtc).ThenBy(task => task.Id).Select(task => task.Id).ToListAsync();
            Assert.Equal(expected, first.Items.Concat(second.Items).Select(task => task.Id));
        }
        var max = (await client.GetFromJsonAsync<ProjectTaskPageResponse>($"{path}?pageSize=100"))!;
        Assert.Equal(25, max.Items.Count);
        Assert.Equal(100, max.PageSize);
        Assert.Equal(1, max.TotalPages);
        var filtered = (await client.GetFromJsonAsync<ProjectTaskPageResponse>($"{path}?search=Pagination&pageSize=10&page=3"))!;
        Assert.Equal(2, filtered.Items.Count);
        Assert.Equal(22, filtered.TotalCount);
        Assert.Equal(3, filtered.TotalPages);
        foreach (var page in new[] { 3, int.MaxValue })
        {
            var empty = (await client.GetFromJsonAsync<ProjectTaskPageResponse>($"{path}?page={page}&pageSize=100"))!;
            Assert.Empty(empty.Items);
            Assert.Equal(page, empty.Page);
            Assert.Equal(25, empty.TotalCount);
            Assert.Equal(1, empty.TotalPages);
        }
    }

    [Theory]
    [InlineData("status=Unknown")]
    [InlineData("status=todo")]
    [InlineData("priority=Urgent")]
    [InlineData("priority=high")]
    [InlineData("assignedToUserId=not-a-guid")]
    [InlineData("page=0")]
    [InlineData("page=-1")]
    [InlineData("page=2147483648")]
    [InlineData("page=abc")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=-1")]
    [InlineData("pageSize=101")]
    [InlineData("pageSize=abc")]
    public async Task InvalidFiltersAndPaginationReturnValidationProblem(string query)
    {
        var (seed, projectId, _) = await SetupAsync(TenantRoles.Member);
        using var client = Client(seed.UserId);
        var response = await client.GetAsync($"{Path(seed.TenantId, projectId)}?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private HttpClient Client(Guid userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.UserIdHeader, userId.ToString());
        return client;
    }

    private static string Path(Guid tenantId, Guid projectId) => $"/api/tenants/{tenantId}/projects/{projectId}/tasks";

    private async Task<(SeededTenant Seed, Guid ProjectId, ProjectTask[] Tasks)> SetupAsync(string role)
    {
        var seed = await factory.SeedTenantAsync(role, createUnrelatedTenant: true);
        var projectId = await factory.SeedProjectAsync(seed.TenantId, seed.UserId);
        var siblingId = await factory.SeedProjectAsync(seed.TenantId, seed.UserId);
        var otherProjectId = await factory.SeedProjectAsync(seed.UnrelatedTenantId, seed.OtherUserId);
        var oldTime = DateTime.UtcNow.AddDays(-1);
        var tasks = new[]
        {
            new ProjectTask { Title = "Alpha release", Status = ProjectTaskStatuses.ToDo, Priority = ProjectTaskPriorities.Low },
            new ProjectTask { Title = "aLPHa follow-up", Status = ProjectTaskStatuses.InProgress, Priority = ProjectTaskPriorities.High, AssignedToUserId = seed.UserId },
            new ProjectTask { Title = "Literal %_ check", Status = ProjectTaskStatuses.Completed, Priority = ProjectTaskPriorities.Medium, AssignedToUserId = seed.OtherUserId }
        };
        foreach (var task in tasks)
        {
            task.TenantId = seed.TenantId;
            task.ProjectId = projectId;
            task.CreatedByUserId = seed.OtherUserId;
            task.Description = "description-only";
            task.DueDateUtc = oldTime.AddDays(7);
            task.CreatedAtUtc = oldTime;
            task.UpdatedAtUtc = oldTime;
        }
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.ProjectTasks.AddRange(tasks);
        db.ProjectTasks.AddRange(
            new ProjectTask
            {
                TenantId = seed.TenantId, ProjectId = siblingId, CreatedByUserId = seed.UserId,
                Title = tasks[1].Title, Status = tasks[1].Status, Priority = tasks[1].Priority,
                AssignedToUserId = seed.UserId
            },
            new ProjectTask
            {
                TenantId = seed.UnrelatedTenantId, ProjectId = otherProjectId, CreatedByUserId = seed.OtherUserId,
                Title = tasks[1].Title, Status = tasks[1].Status, Priority = tasks[1].Priority,
                AssignedToUserId = seed.OtherUserId
            });
        await db.SaveChangesAsync();
        return (seed, projectId, tasks);
    }
}
