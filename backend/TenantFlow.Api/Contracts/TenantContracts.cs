using System.ComponentModel.DataAnnotations;

namespace TenantFlow.Api.Contracts;

/// <summary>Details for creating a tenant owned by the authenticated user.</summary>
public sealed record CreateTenantRequest
{
    [Required]
    [StringLength(150)]
    public required string Name { get; init; }

    [Required]
    [StringLength(100)]
    [RegularExpression("\\A[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\\z", ErrorMessage = "Slug must contain only lowercase letters, numbers, and hyphens, and cannot begin or end with a hyphen.")]
    public required string Slug { get; init; }
}

/// <summary>Tenant details and the authenticated user's role in that tenant.</summary>
public sealed record CreateTenantResponse(Guid TenantId, string Name, string Slug, string Role);

/// <summary>A tenant the authenticated user belongs to, including their role and its creation time.</summary>
public sealed record TenantListItemResponse(
    Guid TenantId,
    string Name,
    string Slug,
    string Role,
    DateTimeOffset CreatedAtUtc);

/// <summary>Details for a tenant the authenticated user belongs to.</summary>
public sealed record TenantDetailsResponse(
    Guid TenantId,
    string Name,
    string Slug,
    string Role,
    DateTimeOffset CreatedAtUtc);

/// <summary>Details for inviting an existing user to a tenant.</summary>
public sealed record CreateTenantInvitationRequest
{
    [Required]
    [EmailAddress]
    public required string Email { get; init; }

    [Required]
    public required string Role { get; init; }
}

/// <summary>Payload for changing a tenant member's role.</summary>
public sealed record UpdateTenantMemberRoleRequest
{
    [Required]
    public required string Role { get; init; }
}

/// <summary>Non-sensitive membership details for a tenant member.</summary>
public sealed record TenantMemberResponse(
    Guid UserId,
    string Email,
    string Role,
    DateTimeOffset CreatedAtUtc);
