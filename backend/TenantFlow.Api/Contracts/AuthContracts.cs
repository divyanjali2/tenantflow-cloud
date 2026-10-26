using System.ComponentModel.DataAnnotations;

namespace TenantFlow.Api.Contracts;

/// <summary>Credentials and email used to register a new account.</summary>
public sealed record RegisterRequest
{
    [Required, EmailAddress, StringLength(256)]
    public required string Email { get; init; }

    [Required, StringLength(128, MinimumLength = 12)]
    public required string Password { get; init; }
}

/// <summary>Credentials used to obtain an access token.</summary>
public sealed record LoginRequest
{
    [Required, EmailAddress, StringLength(256)]
    public required string Email { get; init; }

    [Required, StringLength(128, MinimumLength = 12)]
    public required string Password { get; init; }
}

/// <summary>Identifies a newly registered account.</summary>
public sealed record RegisterResponse(Guid UserId, string Email);

/// <summary>Contains a bearer access token and its lifetime.</summary>
public sealed record LoginResponse(string AccessToken, string TokenType, int ExpiresIn);

/// <summary>Describes the authenticated user's public profile.</summary>
public sealed record UserProfileResponse(Guid UserId, string Email);
