using TenantFlow.Api.Models;

namespace TenantFlow.Api.Services;

public interface IJwtTokenService
{
    (string AccessToken, DateTimeOffset ExpiresAtUtc) CreateAccessToken(ApplicationUser user);
}
