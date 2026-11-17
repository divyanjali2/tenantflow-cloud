using System.Net;
using System.Net.Http.Json;
using TenantFlow.Api.Contracts;
using TenantFlow.Api.Tests.Infrastructure;

namespace TenantFlow.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class DashboardTests(PostgresFixture postgres) : ApiTest(postgres)
{
    [Fact]
    public async Task EmptyTenantReturnsActualZeroStatisticsAndOneOwner()
    {
        var owner = await RegisterAccountAsync();
        var tenant = await CreateTenantAsync(owner);
        var summary = await owner.Client.GetFromJsonAsync<DashboardSummaryResponse>(Url(tenant.TenantId));
        summary.Should().BeEquivalentTo(new DashboardSummaryResponse(0, 0, 0, 0, 0, 0, 0, 0, 1, 0));
    }

    [Fact]
    public async Task SqlAggregatesCountStatusesMembersAndUtcOverdueTasksWithoutTenantLeakage()
    {
        var owner = await RegisterAccountAsync();
        var member = await RegisterAccountAsync();
        var admin = await RegisterAccountAsync();
        var tenant = await CreateTenantAsync(owner);
        await AddMemberAsync(owner, tenant.TenantId, member, "Member");
        await AddMemberAsync(owner, tenant.TenantId, admin, "Admin");
        var project = await CreateProjectAsync(owner, tenant.TenantId, "Active");
        foreach (var status in new[] { "Planning", "Completed", "OnHold", "Cancelled" })
            await CreateProjectAsync(owner, tenant.TenantId, status);
        var now = Factory.Clock.GetUtcNow();
        await CreateTaskAsync(owner, tenant.TenantId, project.Id, status: "ToDo", due: now.AddSeconds(-1));
        await CreateTaskAsync(owner, tenant.TenantId, project.Id, status: "InProgress", due: now.AddDays(-1));
        await CreateTaskAsync(owner, tenant.TenantId, project.Id, status: "Completed", due: now.AddDays(-1));
        await CreateTaskAsync(owner, tenant.TenantId, project.Id, status: "ToDo", due: now);
        await CreateTaskAsync(owner, tenant.TenantId, project.Id, status: "InProgress", due: now.AddDays(1));
        await CreateTaskAsync(owner, tenant.TenantId, project.Id, status: "ToDo");
        await CreateTaskAsync(owner, tenant.TenantId, project.Id, status: "Completed");

        var other = await CreateTenantAsync(owner);
        var foreignProject = await CreateProjectAsync(owner, other.TenantId, "Completed");
        await CreateTaskAsync(owner, other.TenantId, foreignProject.Id, due: now.AddDays(-2));
        var expected = new DashboardSummaryResponse(5, 1, 1, 7, 3, 2, 2, 2, 3, 28.57m);
        foreach (var actor in new[] { owner, admin, member })
            (await actor.Client.GetFromJsonAsync<DashboardSummaryResponse>(Url(tenant.TenantId))).Should().BeEquivalentTo(expected);
        (await owner.Client.GetFromJsonAsync<DashboardSummaryResponse>(Url(other.TenantId)))
            .Should().BeEquivalentTo(new DashboardSummaryResponse(1, 0, 1, 1, 1, 0, 0, 1, 1, 0));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 1, 100)]
    [InlineData(3, 1, 33.33)]
    public async Task CompletionPercentageUsesTheActualTaskDenominator(int total, int completed, decimal percentage)
    {
        var owner = await RegisterAccountAsync();
        var tenant = await CreateTenantAsync(owner);
        var project = await CreateProjectAsync(owner, tenant.TenantId);
        for (var index = 0; index < total; index++)
            await CreateTaskAsync(owner, tenant.TenantId, project.Id, status: index < completed ? "Completed" : "ToDo");
        var summary = (await owner.Client.GetFromJsonAsync<DashboardSummaryResponse>(Url(tenant.TenantId)))!;
        summary.TotalTasks.Should().Be(total);
        summary.CompletedTasks.Should().Be(completed);
        summary.TaskCompletionPercentage.Should().Be(percentage);
    }

    [Fact]
    public async Task SummaryRequiresJwtAndMembershipAndDoesNotCacheSensitiveStatistics()
    {
        var owner = await RegisterAccountAsync();
        var outsider = await RegisterAccountAsync();
        var tenant = await CreateTenantAsync(owner);
        (await Anonymous.GetAsync(Url(tenant.TenantId))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await outsider.Client.GetAsync(Url(tenant.TenantId))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.Client.GetAsync(Url(Guid.NewGuid()))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var response = await owner.Client.GetAsync(Url(tenant.TenantId));
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }
    private static string Url(Guid tenant) => $"/api/tenants/{tenant}/dashboard/summary";
}
