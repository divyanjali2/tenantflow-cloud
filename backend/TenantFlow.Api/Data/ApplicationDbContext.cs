using Microsoft.EntityFrameworkCore;
using TenantFlow.Api.Models;

namespace TenantFlow.Api.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<ApplicationUser> Users => Set<ApplicationUser>();
    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.Property(tenant => tenant.Name).IsRequired();
            entity.Property(tenant => tenant.Slug).IsRequired();
            entity.HasIndex(tenant => tenant.Slug).IsUnique();
            entity.HasMany(tenant => tenant.Memberships)
                .WithOne(membership => membership.Tenant)
                .HasForeignKey(membership => membership.TenantId);
        });

        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(user => user.Email).IsRequired();
            entity.HasIndex(user => user.Email).IsUnique();
            entity.HasMany(user => user.Memberships)
                .WithOne(membership => membership.User)
                .HasForeignKey(membership => membership.UserId);
        });

        modelBuilder.Entity<TenantMembership>()
            .HasIndex(membership => new { membership.TenantId, membership.UserId })
            .IsUnique();
    }
}
