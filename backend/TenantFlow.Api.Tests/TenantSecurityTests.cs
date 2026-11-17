using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenantFlow.Api.Contracts;
using TenantFlow.Api.Data;
using TenantFlow.Api.Tests.Infrastructure;

namespace TenantFlow.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class TenantSecurityTests(PostgresFixture postgres) : ApiTest(postgres)
{
    [Fact]
    public async Task CreatorBecomesOwnerAndCanSelectTheirTenant()
    {
        var owner = await RegisterAccountAsync();
        var tenant = await CreateTenantAsync(owner);
        tenant.Role.Should().Be("Owner");
        (await owner.Client.GetAsync($"/api/tenants/{tenant.TenantId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var mine = (await owner.Client.GetFromJsonAsync<TenantListItemResponse[]>("/api/tenants/mine"))!;
        mine.Should().ContainSingle().Which.TenantId.Should().Be(tenant.TenantId);
        var selection = await owner.Client.PostAsync($"/api/tenants/{tenant.TenantId}/select", null);
        selection.StatusCode.Should().Be(HttpStatusCode.OK);
        (await selection.Content.ReadFromJsonAsync<CreateTenantResponse>())!.TenantId.Should().Be(tenant.TenantId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/members")]
    [InlineData("/projects")]
    [InlineData("/dashboard/summary")]
    public async Task NonmemberReceivesNotFoundForTenantResources(string suffix)
    {
        var owner = await RegisterAccountAsync();
        var outsider = await RegisterAccountAsync();
        var tenant = await CreateTenantAsync(owner);
        (await outsider.Client.GetAsync($"/api/tenants/{tenant.TenantId}{suffix}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await outsider.Client.GetFromJsonAsync<TenantListItemResponse[]>("/api/tenants/mine"))!.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Member")]
    public async Task NonOwnersCannotAddRemoveOrChangeMemberRoles(string role)
    {
        var owner = await RegisterAccountAsync();
        var actor = await RegisterAccountAsync();
        var target = await RegisterAccountAsync();
        var tenant = await CreateTenantAsync(owner);
        await AddMemberAsync(owner, tenant.TenantId, actor, role);
        var url = $"/api/tenants/{tenant.TenantId}";
        (await actor.Client.PostAsJsonAsync($"{url}/invitations", new { email = target.Email, role = "Member" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await actor.Client.PatchAsJsonAsync($"{url}/members/{owner.UserId}/role", new { role = "Member" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await actor.Client.DeleteAsync($"{url}/members/{owner.UserId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OwnerAccountCannotBeRemovedOrDemoted()
    {
        var owner = await RegisterAccountAsync();
        var tenant = await CreateTenantAsync(owner);
        var url = $"/api/tenants/{tenant.TenantId}/members/{owner.UserId}";
        (await owner.Client.DeleteAsync(url)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await owner.Client.PatchAsJsonAsync($"{url}/role", new { role = "Admin" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var members = (await owner.Client.GetFromJsonAsync<TenantMemberResponse[]>($"/api/tenants/{tenant.TenantId}/members"))!;
        members.Should().ContainSingle().Which.Role.Should().Be("Owner");
    }

    [Fact]
    public async Task MembershipChangesPersistAndRemovalPreservesOtherTenantAndUserData()
    {
        var owner = await RegisterAccountAsync();
        var member = await RegisterAccountAsync();
        var first = await CreateTenantAsync(owner);
        var second = await CreateTenantAsync(owner);
        await AddMemberAsync(owner, first.TenantId, member, "Member");
        await AddMemberAsync(owner, second.TenantId, member, "Member");
        var url = $"/api/tenants/{first.TenantId}/members/{member.UserId}";
        var promote = await owner.Client.PatchAsJsonAsync($"{url}/role", new { role = "Admin" });
        promote.StatusCode.Should().Be(HttpStatusCode.OK);
        (await promote.Content.ReadFromJsonAsync<TenantMemberResponse>())!.Role.Should().Be("Admin");
        (await owner.Client.DeleteAsync(url)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await member.Client.GetAsync($"/api/tenants/{first.TenantId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await member.Client.GetAsync($"/api/tenants/{second.TenantId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await member.Client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DuplicateMembershipReturnsConflictWithoutDuplicatingRows()
    {
        var owner = await RegisterAccountAsync();
        var member = await RegisterAccountAsync();
        var tenant = await CreateTenantAsync(owner);
        await AddMemberAsync(owner, tenant.TenantId, member, "Member");
        (await owner.Client.PostAsJsonAsync($"/api/tenants/{tenant.TenantId}/invitations", new { email = member.Email, role = "Admin" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using var scope = Factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().TenantMemberships.CountAsync()).Should().Be(2);
    }
}
