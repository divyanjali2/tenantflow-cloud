namespace TenantFlow.Api.Models;

public class Tenant
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<TenantMembership> Memberships { get; set; } = new List<TenantMembership>();
}
