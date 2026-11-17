using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenantFlow.Api.Contracts;
using TenantFlow.Api.Data;

namespace TenantFlow.Api.Tests.Infrastructure;

public abstract class ApiTest(PostgresFixture postgres) : IAsyncLifetime
{
    protected PostgresFixture DatabaseOwner => postgres;
    private string? connectionString;
    protected ApiFactory Factory { get; private set; } = null!;
    protected HttpClient Anonymous { get; private set; } = null!;
    private readonly List<HttpClient> clients = [];

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateDatabaseAsync();
        try
        {
            Factory = new ApiFactory(postgres, connectionString);
            Factory.AssertTestConfiguration();
            Anonymous = NewClient();
            await using var scope = Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            postgres.AssertOwned(db.Database.GetConnectionString()!);
            await db.Database.MigrateAsync();
        }
        catch { await DisposeAsync(); throw; }
    }

    protected HttpClient NewClient()
    {
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });
        clients.Add(client);
        return client;
    }

    protected static string NewPassword() => $"aA1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(20))}";

    protected async Task<Account> RegisterAccountAsync()
    {
        var email = $"user-{Guid.NewGuid():N}@example.test";
        var password = NewPassword();
        var register = await Anonymous.PostAsJsonAsync("/api/auth/register", new { email, password });
        register.StatusCode.Should().Be(HttpStatusCode.Created);
        var registered = (await register.Content.ReadFromJsonAsync<RegisterResponse>())!;
        var login = await Anonymous.PostAsJsonAsync("/api/auth/login", new { email, password });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var credentials = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        var client = NewClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        return new Account(registered.UserId, email, password, client);
    }

    protected static async Task<CreateTenantResponse> CreateTenantAsync(Account owner)
    {
        var response = await owner.Client.PostAsJsonAsync("/api/tenants", new { name = "Integration workspace", slug = $"test-{Guid.NewGuid():N}" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CreateTenantResponse>())!;
    }

    protected static async Task AddMemberAsync(Account owner, Guid tenantId, Account member, string role)
    {
        var response = await owner.Client.PostAsJsonAsync($"/api/tenants/{tenantId}/invitations", new { email = member.Email, role });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    protected static async Task<ProjectResponse> CreateProjectAsync(Account owner, Guid tenantId, string status = "Planning", string name = "Integration project")
    {
        var response = await owner.Client.PostAsJsonAsync($"/api/tenants/{tenantId}/projects", new { name, status });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ProjectResponse>())!;
    }

    protected static async Task<ProjectTaskResponse> CreateTaskAsync(Account owner, Guid tenantId, Guid projectId,
        string title = "Integration task", string status = "ToDo", string priority = "Medium", Guid? assignee = null, DateTimeOffset? due = null)
    {
        var response = await owner.Client.PostAsJsonAsync(TasksPath(tenantId, projectId), new
        {
            title, status, priority, assignedToUserId = assignee, dueDateUtc = due
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ProjectTaskResponse>())!;
    }
    protected static string TasksPath(Guid tenantId, Guid projectId) => $"/api/tenants/{tenantId}/projects/{projectId}/tasks";

    public async Task DisposeAsync()
    {
        try
        {
            foreach (var client in clients) client.Dispose();
            if (Factory is not null) await Factory.DisposeAsync();
        }
        finally
        {
            if (connectionString is not null)
            {
                var owned = connectionString;
                connectionString = null;
                await postgres.DropDatabaseAsync(owned);
            }
        }
    }
}

// Avoid an auto-generated record ToString that could print passwords on assertion failures.
public sealed class Account(Guid userId, string email, string password, HttpClient client)
{
    public Guid UserId { get; } = userId;
    public string Email { get; } = email;
    public string Password { get; } = password;
    public HttpClient Client { get; } = client;
}
