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
[Route("api/tenants/{tenantId:guid}/projects/{projectId:guid}/tasks")]
public sealed class ProjectTasksController(
    ApplicationDbContext dbContext,
    ITenantAuthorizationService tenantAuthorizationService) : ControllerBase
{
    private static readonly string[] WriterRoles = [TenantRoles.Owner, TenantRoles.Admin];

    [HttpPost]
    public async Task<ActionResult<ProjectTaskResponse>> CreateTask(
        Guid tenantId, Guid projectId, CreateProjectTaskRequest request, CancellationToken cancellationToken)
    {
        if (await CheckProjectAccessAsync(tenantId, projectId, true, cancellationToken) is { } error)
            return error;
        if (!await ValidateRequestAsync(tenantId, request, cancellationToken))
            return ValidationProblem(ModelState);

        var now = DateTime.UtcNow;
        var task = new ProjectTask
        {
            TenantId = tenantId,
            ProjectId = projectId,
            Title = request.Title.Trim(),
            CreatedByUserId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        ApplyRequest(task, request);
        dbContext.ProjectTasks.Add(task);
        await dbContext.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetTaskById), new { tenantId, projectId, taskId = task.Id }, ToResponse(task));
    }

    [HttpGet]
    public async Task<ActionResult<ProjectTaskPageResponse>> GetTasks(
        Guid tenantId, Guid projectId, [FromQuery] ProjectTaskListQuery query, CancellationToken cancellationToken)
    {
        if (await CheckProjectAccessAsync(tenantId, projectId, false, cancellationToken) is { } error)
            return error;
        if (query.Status is not null && !ProjectTaskStatuses.All.Contains(query.Status, StringComparer.Ordinal))
            ModelState.AddModelError(nameof(query.Status), "Status must be ToDo, InProgress or Completed.");
        if (query.Priority is not null && !ProjectTaskPriorities.All.Contains(query.Priority, StringComparer.Ordinal))
            ModelState.AddModelError(nameof(query.Priority), "Priority must be Low, Medium or High.");
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var tasks = ScopedTasks(tenantId, projectId).AsNoTracking();
        if (query.Status is not null) tasks = tasks.Where(task => task.Status == query.Status);
        if (query.Priority is not null) tasks = tasks.Where(task => task.Priority == query.Priority);
        if (query.AssignedToUserId is { } assigneeId)
            tasks = tasks.Where(task => task.AssignedToUserId == assigneeId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // EF translates this to a case-insensitive literal substring search on PostgreSQL.
            var search = query.Search.Trim().ToLowerInvariant();
            tasks = tasks.Where(task => task.Title.ToLower().Contains(search));
        }

        var totalCount = await tasks.CountAsync(cancellationToken);
        var totalPages = (int)Math.Ceiling(totalCount / (double)query.PageSize);
        // Large valid page numbers return an empty page without overflowing Skip's int offset.
        var offset = ((long)query.Page - 1) * query.PageSize;
        var items = offset >= totalCount
            ? new List<ProjectTaskResponse>()
            : await tasks.OrderByDescending(task => task.CreatedAtUtc).ThenBy(task => task.Id)
                .Skip((int)offset).Take(query.PageSize)
                .Select(task => ToResponse(task)).ToListAsync(cancellationToken);
        return Ok(new ProjectTaskPageResponse(items, query.Page, query.PageSize, totalCount, totalPages));
    }

    [HttpGet("{taskId:guid}")]
    public async Task<ActionResult<ProjectTaskResponse>> GetTaskById(
        Guid tenantId, Guid projectId, Guid taskId, CancellationToken cancellationToken)
    {
        if (await CheckProjectAccessAsync(tenantId, projectId, false, cancellationToken) is { } error)
            return error;
        var task = await ScopedTasks(tenantId, projectId).AsNoTracking()
            .FirstOrDefaultAsync(task => task.Id == taskId, cancellationToken);
        return task is null ? NotFound() : Ok(ToResponse(task));
    }

    [HttpPut("{taskId:guid}")]
    public async Task<ActionResult<ProjectTaskResponse>> UpdateTask(
        Guid tenantId, Guid projectId, Guid taskId, UpdateProjectTaskRequest request,
        CancellationToken cancellationToken)
    {
        if (await CheckProjectAccessAsync(tenantId, projectId, true, cancellationToken) is { } error)
            return error;
        var task = await ScopedTasks(tenantId, projectId)
            .FirstOrDefaultAsync(task => task.Id == taskId, cancellationToken);
        if (task is null) return NotFound();
        if (!await ValidateRequestAsync(tenantId, request, cancellationToken))
            return ValidationProblem(ModelState);
        ApplyRequest(task, request);
        task.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(task));
    }

    [HttpDelete("{taskId:guid}")]
    public async Task<IActionResult> DeleteTask(
        Guid tenantId, Guid projectId, Guid taskId, CancellationToken cancellationToken)
    {
        if (await CheckProjectAccessAsync(tenantId, projectId, true, cancellationToken) is { } error)
            return error;
        var task = await ScopedTasks(tenantId, projectId)
            .FirstOrDefaultAsync(task => task.Id == taskId, cancellationToken);
        if (task is null) return NotFound();
        dbContext.ProjectTasks.Remove(task);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPatch("{taskId:guid}/assignee")]
    public async Task<ActionResult<ProjectTaskResponse>> UpdateTaskAssignee(
        Guid tenantId, Guid projectId, Guid taskId, UpdateProjectTaskAssigneeRequest request,
        CancellationToken cancellationToken)
    {
        if (await CheckProjectAccessAsync(tenantId, projectId, false, cancellationToken) is { } error)
            return error;
        var task = await ScopedTasks(tenantId, projectId)
            .FirstOrDefaultAsync(task => task.Id == taskId, cancellationToken);
        if (task is null) return NotFound();
        var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        if (!await tenantAuthorizationService.HasAnyRoleAsync(userId, tenantId, WriterRoles, cancellationToken))
            return Forbid();
        await ValidateAssigneeAsync(tenantId, request.AssignedToUserId, cancellationToken);
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        task.AssignedToUserId = request.AssignedToUserId;
        task.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(task));
    }

    [HttpPatch("{taskId:guid}/status")]
    public async Task<ActionResult<ProjectTaskResponse>> UpdateTaskStatus(
        Guid tenantId, Guid projectId, Guid taskId, UpdateProjectTaskStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (await CheckProjectAccessAsync(tenantId, projectId, false, cancellationToken) is { } error)
            return error;
        var task = await ScopedTasks(tenantId, projectId)
            .FirstOrDefaultAsync(task => task.Id == taskId, cancellationToken);
        if (task is null) return NotFound();
        var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        IReadOnlyCollection<string> allowedRoles = task.AssignedToUserId == userId ? TenantRoles.All : WriterRoles;
        if (!await tenantAuthorizationService.HasAnyRoleAsync(userId, tenantId, allowedRoles, cancellationToken))
            return Forbid();
        if (!ProjectTaskStatuses.All.Contains(request.Status, StringComparer.Ordinal))
        {
            ModelState.AddModelError(nameof(request.Status), "Status must be ToDo, InProgress or Completed.");
            return ValidationProblem(ModelState);
        }

        task.Status = request.Status;
        task.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(task));
    }

    private IQueryable<ProjectTask> ScopedTasks(Guid tenantId, Guid projectId) =>
        dbContext.ProjectTasks.Where(task => task.TenantId == tenantId && task.ProjectId == projectId);

    private async Task<ActionResult?> CheckProjectAccessAsync(
        Guid tenantId, Guid projectId, bool write, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId))
            return Unauthorized();
        if (!await tenantAuthorizationService.IsMemberAsync(userId, tenantId, cancellationToken))
            return NotFound();
        if (write && !await tenantAuthorizationService.HasAnyRoleAsync(userId, tenantId, WriterRoles, cancellationToken))
            return Forbid();
        if (!await dbContext.Projects.AsNoTracking()
                .AnyAsync(project => project.TenantId == tenantId && project.Id == projectId, cancellationToken))
            return NotFound();
        return null;
    }

    private async Task<bool> ValidateRequestAsync(
        Guid tenantId, ProjectTaskRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200)
            ModelState.AddModelError(nameof(request.Title), "Title must contain between 1 and 200 characters.");
        if (!ProjectTaskStatuses.All.Contains(request.Status, StringComparer.Ordinal))
            ModelState.AddModelError(nameof(request.Status), "Status must be ToDo, InProgress or Completed.");
        if (!ProjectTaskPriorities.All.Contains(request.Priority, StringComparer.Ordinal))
            ModelState.AddModelError(nameof(request.Priority), "Priority must be Low, Medium or High.");
        await ValidateAssigneeAsync(tenantId, request.AssignedToUserId, cancellationToken);
        return ModelState.IsValid;
    }

    private async Task ValidateAssigneeAsync(Guid tenantId, Guid? assigneeId, CancellationToken cancellationToken)
    {
        // Memberships are active while their row exists; removal deletes the row in the existing API.
        if (assigneeId is { } userId
            && !await tenantAuthorizationService.IsMemberAsync(userId, tenantId, cancellationToken))
            ModelState.AddModelError(nameof(ProjectTaskRequest.AssignedToUserId), "Assignee must be a current member of this tenant.");
    }

    private static void ApplyRequest(ProjectTask task, ProjectTaskRequest request)
    {
        task.Title = request.Title.Trim();
        task.Description = request.Description;
        task.Status = request.Status;
        task.Priority = request.Priority;
        task.AssignedToUserId = request.AssignedToUserId;
        task.DueDateUtc = request.DueDateUtc?.UtcDateTime;
    }

    private static ProjectTaskResponse ToResponse(ProjectTask task) => new(
        task.Id, task.TenantId, task.ProjectId, task.Title, task.Description,
        task.Status, task.Priority, task.AssignedToUserId, task.DueDateUtc,
        task.CreatedByUserId, task.CreatedAtUtc, task.UpdatedAtUtc);
}
