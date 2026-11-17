using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TenantFlow.Api.Models;

namespace TenantFlow.Api.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectTask> ProjectTasks => Set<ProjectTask>();

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
            entity.ToTable("Users");
            entity.Property(user => user.Email)
                .IsRequired()
                .HasColumnType("text");
            entity.HasIndex(user => user.Email).IsUnique();
            entity.HasMany(user => user.Memberships)
                .WithOne(membership => membership.User)
                .HasForeignKey(membership => membership.UserId);
        });

        modelBuilder.Entity<TenantMembership>()
            .HasIndex(membership => new { membership.TenantId, membership.UserId })
            .IsUnique();

        modelBuilder.Entity<ProjectTask>(entity =>
        {
            entity.Property(task => task.Title).IsRequired().HasMaxLength(200);
            entity.Property(task => task.Status).IsRequired().HasMaxLength(20)
                .HasDefaultValue(ProjectTaskStatuses.ToDo);
            entity.Property(task => task.Priority).IsRequired().HasMaxLength(20)
                .HasDefaultValue(ProjectTaskPriorities.Medium);
            entity.HasIndex(task => task.TenantId);
            entity.HasIndex(task => task.ProjectId);
            entity.HasIndex(task => task.AssignedToUserId);
            entity.HasOne(task => task.Tenant).WithMany()
                .HasForeignKey(task => task.TenantId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(task => task.Project).WithMany()
                .HasForeignKey(task => task.ProjectId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(task => task.CreatedByUser).WithMany()
                .HasForeignKey(task => task.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(task => task.AssignedToUser).WithMany()
                .HasForeignKey(task => task.AssignedToUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Project>(entity =>
        {
            entity.Property(project => project.Name)
                .IsRequired()
                .HasMaxLength(200);
            entity.Property(project => project.Status)
                .IsRequired()
                .HasMaxLength(20)
                .HasDefaultValue(ProjectStatuses.Planning);
            entity.HasIndex(project => project.TenantId);
            entity.HasOne(project => project.Tenant)
                .WithMany()
                .HasForeignKey(project => project.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(project => project.CreatedByUser)
                .WithMany()
                .HasForeignKey(project => project.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
