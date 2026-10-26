namespace TenantFlow.Api.Authorization;

public interface ITenantAuthorizationService
{
    Task<bool> IsMemberAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<bool> HasAnyRoleAsync(
        Guid userId,
        Guid tenantId,
        IReadOnlyCollection<string> allowedRoles,
        CancellationToken cancellationToken);
}
