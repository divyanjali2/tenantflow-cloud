using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using TenantFlow.Api.Configuration;
using TenantFlow.Api.Models;

namespace TenantFlow.Api.Services;

public sealed class JwtTokenService(JwtSettings settings) : IJwtTokenService
{
    public (string AccessToken, DateTimeOffset ExpiresAtUtc) CreateAccessToken(ApplicationUser user)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAtUtc = now.AddMinutes(60);
        var email = user.Email
            ?? throw new InvalidOperationException("Cannot create an access token for a user without an email.");

        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString("D")),
                new Claim(JwtRegisteredClaimNames.Email, email)
            ],
            notBefore: now.UtcDateTime,
            expires: expiresAtUtc.UtcDateTime,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)),
                SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }
}
