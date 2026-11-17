using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenantFlow.Api.Authorization;
using TenantFlow.Api.Contracts;
using TenantFlow.Api.Data;
using TenantFlow.Api.Models;

namespace TenantFlow.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/tenants/{tenantId:guid}/dashboard")]
public sealed class DashboardController(
    ApplicationDbContext dbContext,
    ITenantAuthorizationService tenantAuthorizationService,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet("summary")]
    [ProducesResponseType<DashboardSummaryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<DashboardSummaryResponse>> GetSummary(
        Guid tenantId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId))
            return Unauthorized();
        // Membership's tenant FK also guarantees that the requested tenant exists.
        if (!await tenantAuthorizationService.IsMemberAsync(userId, tenantId, cancellationToken))
            return NotFound();

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        // Aggregate in PostgreSQL, returning one row per entity type, not entity lists.
        var projects = await dbContext.Projects.AsNoTracking()
            .Where(project => project.TenantId == tenantId)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Total = group.Count(),
                Active = group.Count(project => project.Status == ProjectStatuses.Active),
                Completed = group.Count(project => project.Status == ProjectStatuses.Completed)
            }).FirstOrDefaultAsync(cancellationToken);
        var tasks = await dbContext.ProjectTasks.AsNoTracking()
            .Where(task => task.TenantId == tenantId)
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Total = group.Count(),
                Todo = group.Count(task => task.Status == ProjectTaskStatuses.ToDo),
                InProgress = group.Count(task => task.Status == ProjectTaskStatuses.InProgress),
                Completed = group.Count(task => task.Status == ProjectTaskStatuses.Completed),
                Overdue = group.Count(task => task.DueDateUtc.HasValue
                    && task.DueDateUtc.Value < nowUtc && task.Status != ProjectTaskStatuses.Completed)
            }).FirstOrDefaultAsync(cancellationToken);
        var totalMembers = await dbContext.TenantMemberships.AsNoTracking()
            .CountAsync(membership => membership.TenantId == tenantId, cancellationToken);
        var totalTasks = tasks?.Total ?? 0;
        var completedTasks = tasks?.Completed ?? 0;
        var percentage = totalTasks == 0 ? 0m
            : Math.Round(completedTasks * 100m / totalTasks, 2, MidpointRounding.AwayFromZero);

        return Ok(new DashboardSummaryResponse(
            projects?.Total ?? 0, projects?.Active ?? 0, projects?.Completed ?? 0,
            totalTasks, tasks?.Todo ?? 0, tasks?.InProgress ?? 0, completedTasks,
            tasks?.Overdue ?? 0, totalMembers, percentage));
    }
}
