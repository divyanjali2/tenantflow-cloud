using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TenantFlow.Api.Contracts;
using TenantFlow.Api.Data;
using TenantFlow.Api.Tests.Infrastructure;

namespace TenantFlow.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class AuthenticationTests(PostgresFixture postgres) : ApiTest(postgres)
{
    [Fact]
    public async Task RegistrationPersistsInitializedIdentityFieldsInPostgres()
    {
        var email = $"registration-{Guid.NewGuid():N}@example.test";
        var response = await Anonymous.PostAsJsonAsync("/api/auth/register", new { email, password = NewPassword() });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var registered = (await response.Content.ReadFromJsonAsync<RegisterResponse>())!;
        registered.UserId.Should().NotBeEmpty();
        registered.Email.Should().Be(email);
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync();
        user.Id.Should().Be(registered.UserId);
        user.NormalizedEmail.Should().Be(email.ToUpperInvariant());
        user.NormalizedUserName.Should().Be(email.ToUpperInvariant());
        string.IsNullOrWhiteSpace(user.SecurityStamp).Should().BeFalse();
        string.IsNullOrWhiteSpace(user.ConcurrencyStamp).Should().BeFalse();
        string.IsNullOrWhiteSpace(user.PasswordHash).Should().BeFalse();
    }

    [Fact]
    public async Task DuplicateNormalizedEmailCannotCreateAnotherAccount()
    {
        var account = await RegisterAccountAsync();
        var duplicate = await Anonymous.PostAsJsonAsync("/api/auth/register", new { email = account.Email.ToUpperInvariant(), password = NewPassword() });
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using var scope = Factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task SuccessfulLoginGrantsAccessToMeWithARealJwt()
    {
        var account = await RegisterAccountAsync();
        var response = await account.Client.GetAsync("/api/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = (await response.Content.ReadFromJsonAsync<UserProfileResponse>())!;
        profile.UserId.Should().Be(account.UserId);
        profile.Email.Should().Be(account.Email);
    }

    [Fact]
    public async Task InvalidPasswordIsRejectedWithoutGrantingAClientSession()
    {
        var account = await RegisterAccountAsync();
        var response = await Anonymous.PostAsJsonAsync("/api/auth/login", new { email = account.Email, password = NewPassword() });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Anonymous.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public async Task InvalidRegistrationEmailsReturnBadRequest(string email)
    {
        (await Anonymous.PostAsJsonAsync("/api/auth/register", new { email, password = NewPassword() }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("/api/auth/me")]
    [InlineData("/api/tenants/mine")]
    public async Task AnonymousRequestsCannotAccessProtectedEndpoints(string path)
    {
        (await Anonymous.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CorruptedJwtIsRejected()
    {
        var account = await RegisterAccountAsync();
        var original = account.Client.DefaultRequestHeaders.Authorization!.Parameter!;
        var segments = original.Split('.');
        segments[2] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        account.Client.DefaultRequestHeaders.Authorization = new("Bearer", string.Join('.', segments));
        (await account.Client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
