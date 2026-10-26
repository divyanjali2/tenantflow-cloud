using System.Text;

namespace TenantFlow.Api.Configuration;

public sealed record JwtSettings(string Key, string Issuer, string Audience)
{
    public static JwtSettings FromConfiguration(IConfiguration configuration)
    {
        var key = configuration["Jwt:Key"];
        var issuer = configuration["Jwt:Issuer"];
        var audience = configuration["Jwt:Audience"];

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("JWT configuration 'Jwt:Key' is required.");
        }

        if (Encoding.UTF8.GetByteCount(key) < 32)
        {
            throw new InvalidOperationException("JWT configuration 'Jwt:Key' must be at least 32 UTF-8 bytes.");
        }

        if (string.IsNullOrWhiteSpace(issuer))
        {
            throw new InvalidOperationException("JWT configuration 'Jwt:Issuer' is required.");
        }

        if (string.IsNullOrWhiteSpace(audience))
        {
            throw new InvalidOperationException("JWT configuration 'Jwt:Audience' is required.");
        }

        return new JwtSettings(key, issuer, audience);
    }
}
