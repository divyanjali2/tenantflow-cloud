using System.ComponentModel.DataAnnotations;
using TenantFlow.Api.Models;

namespace TenantFlow.Api.Contracts;

// Only mutable fields are accepted. Tenant, project, creator and timestamps come from the server.
public sealed record CreateProjectTaskRequest : ProjectTaskRequest;
public sealed record UpdateProjectTaskRequest : ProjectTaskRequest;

public sealed record UpdateProjectTaskAssigneeRequest
{
    // Required in JSON, but explicitly nullable to support unassignment.
    public required Guid? AssignedToUserId { get; init; }
}

public sealed record UpdateProjectTaskStatusRequest
{
    [Required]
    public required string Status { get; init; }
}

public sealed record ProjectTaskListQuery
{
    public string? Status { get; init; }
    public string? Priority { get; init; }
    public Guid? AssignedToUserId { get; init; }
    public string? Search { get; init; }

    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, 100)]
    public int PageSize { get; init; } = 20;
}

public sealed record ProjectTaskPageResponse(
    IReadOnlyList<ProjectTaskResponse> Items, int Page, int PageSize, int TotalCount, int TotalPages);

public abstract record ProjectTaskRequest
{
    [Required]
    [StringLength(200)]
    public required string Title { get; init; }
    public string? Description { get; init; }
    public string Status { get; init; } = ProjectTaskStatuses.ToDo;
    public string Priority { get; init; } = ProjectTaskPriorities.Medium;
    public Guid? AssignedToUserId { get; init; }
    public DateTimeOffset? DueDateUtc { get; init; }
}

public sealed record ProjectTaskResponse(
    Guid Id, Guid TenantId, Guid ProjectId, string Title, string? Description,
    string Status, string Priority, Guid? AssignedToUserId, DateTime? DueDateUtc,
    Guid CreatedByUserId, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);
