namespace TenantFlow.Api.Models;

public class TenantMembership
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Tenant Tenant { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;
}
