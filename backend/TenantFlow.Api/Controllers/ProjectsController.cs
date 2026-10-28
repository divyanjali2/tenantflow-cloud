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
[Route("api/tenants/{tenantId:guid}/projects")]
public sealed class ProjectsController(
    ApplicationDbContext dbContext,
    ITenantAuthorizationService tenantAuthorizationService) : ControllerBase
{
    private static readonly string[] ProjectWriterRoles = [TenantRoles.Owner, TenantRoles.Admin];
    private static readonly string[] ProjectOwnerRoles = [TenantRoles.Owner];

    [HttpPost]
    [ProducesResponseType<ProjectResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectResponse>> CreateProject(
        Guid tenantId,
        CreateProjectRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId))
        {
            return Unauthorized();
        }

        if (!await tenantAuthorizationService.IsMemberAsync(userId, tenantId, cancellationToken))
        {
            return NotFound();
        }

        if (!await tenantAuthorizationService.HasAnyRoleAsync(
                userId,
                tenantId,
                ProjectWriterRoles,
                cancellationToken))
        {
            return Forbid();
        }

        if (!IsValidStatus(request.Status))
        {
            ModelState.AddModelError(nameof(request.Status), "Status is not supported.");
            return ValidationProblem(ModelState);
        }

        var now = DateTime.UtcNow;
        var project = new Project
        {
            TenantId = tenantId,
            Name = request.Name.Trim(),
            Description = request.Description,
            Status = request.Status,
            CreatedByUserId = userId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync(cancellationToken);

        var response = ToResponse(project);
        return CreatedAtAction(
            nameof(GetProjectById),
            new { tenantId, projectId = project.Id },
            response);
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ProjectResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ProjectResponse>>> GetProjects(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId))
        {
            return Unauthorized();
        }

        if (!await tenantAuthorizationService.IsMemberAsync(userId, tenantId, cancellationToken))
        {
            return NotFound();
        }

        var projects = await dbContext.Projects
            .AsNoTracking()
            .Where(project => project.TenantId == tenantId)
            .OrderByDescending(project => project.CreatedAtUtc)
            .Select(project => ToResponse(project))
            .ToListAsync(cancellationToken);

        return Ok(projects);
    }

    [HttpGet("{projectId:guid}")]
    [ProducesResponseType<ProjectResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectResponse>> GetProjectById(
        Guid tenantId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId))
        {
            return Unauthorized();
        }

        if (!await tenantAuthorizationService.IsMemberAsync(userId, tenantId, cancellationToken))
        {
            return NotFound();
        }

        var project = await dbContext.Projects
            .AsNoTracking()
            .Where(project => project.TenantId == tenantId && project.Id == projectId)
            .Select(project => ToResponse(project))
            .FirstOrDefaultAsync(cancellationToken);

        return project is null ? NotFound() : Ok(project);
    }

    [HttpPut("{projectId:guid}")]
    [ProducesResponseType<ProjectResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectResponse>> UpdateProject(
        Guid tenantId,
        Guid projectId,
        UpdateProjectRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId))
        {
            return Unauthorized();
        }

        if (!await tenantAuthorizationService.IsMemberAsync(userId, tenantId, cancellationToken))
        {
            return NotFound();
        }

        if (!await tenantAuthorizationService.HasAnyRoleAsync(
                userId,
                tenantId,
                ProjectWriterRoles,
                cancellationToken))
        {
            return Forbid();
        }

        if (!IsValidStatus(request.Status))
        {
            ModelState.AddModelError(nameof(request.Status), "Status is not supported.");
            return ValidationProblem(ModelState);
        }

        var project = await dbContext.Projects
            .FirstOrDefaultAsync(
                entry => entry.TenantId == tenantId && entry.Id == projectId,
                cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        project.Name = request.Name.Trim();
        project.Description = request.Description;
        project.Status = request.Status;
        project.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(project));
    }

    [HttpDelete("{projectId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteProject(
        Guid tenantId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId))
        {
            return Unauthorized();
        }

        if (!await tenantAuthorizationService.IsMemberAsync(userId, tenantId, cancellationToken))
        {
            return NotFound();
        }

        if (!await tenantAuthorizationService.HasAnyRoleAsync(
                userId,
                tenantId,
                ProjectOwnerRoles,
                cancellationToken))
        {
            return Forbid();
        }

        var project = await dbContext.Projects
            .FirstOrDefaultAsync(
                entry => entry.TenantId == tenantId && entry.Id == projectId,
                cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        dbContext.Projects.Remove(project);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private bool TryGetAuthenticatedUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);

    private static bool IsValidStatus(string? status) =>
        status is not null && ProjectStatuses.All.Contains(status, StringComparer.Ordinal);

    private static ProjectResponse ToResponse(Project project) =>
        new(
            project.Id,
            project.TenantId,
            project.Name,
            project.Description,
            project.Status,
            project.CreatedByUserId,
            new DateTimeOffset(DateTime.SpecifyKind(project.CreatedAtUtc, DateTimeKind.Utc)),
            new DateTimeOffset(DateTime.SpecifyKind(project.UpdatedAtUtc, DateTimeKind.Utc)));
}
