namespace TenantFlow.Api.Models;

public class ApplicationUser
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<TenantMembership> Memberships { get; set; } = new List<TenantMembership>();
}
