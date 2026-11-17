using Microsoft.EntityFrameworkCore;
using TenantFlow.Api.Data;

namespace TenantFlow.Api.Authorization;

public sealed class TenantAuthorizationService(ApplicationDbContext dbContext)
    : ITenantAuthorizationService
{
    public Task<bool> IsMemberAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken) =>
        dbContext.TenantMemberships
            .AsNoTracking()
            .AnyAsync(
                membership => membership.UserId == userId && membership.TenantId == tenantId,
                cancellationToken);

    public Task<bool> HasAnyRoleAsync(
        Guid userId,
        Guid tenantId,
        IReadOnlyCollection<string> allowedRoles,
        CancellationToken cancellationToken) =>
        dbContext.TenantMemberships
            .AsNoTracking()
            .AnyAsync(
                membership =>
                    membership.UserId == userId
                    && membership.TenantId == tenantId
                    && allowedRoles.Contains(membership.Role),
                cancellationToken);
}
