using Microsoft.AspNetCore.Identity;

namespace TenantFlow.Api.Models;

public class ApplicationUser : IdentityUser<Guid>
{
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<TenantMembership> Memberships { get; set; } = new List<TenantMembership>();
}
