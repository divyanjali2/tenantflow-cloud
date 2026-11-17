namespace TenantFlow.Api.Models;

public sealed class ProjectTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = ProjectTaskStatuses.ToDo;
    public string Priority { get; set; } = ProjectTaskPriorities.Medium;
    public Guid? AssignedToUserId { get; set; }
    public DateTime? DueDateUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public Tenant Tenant { get; set; } = null!;
    public Project Project { get; set; } = null!;
    public ApplicationUser? AssignedToUser { get; set; }
    public ApplicationUser CreatedByUser { get; set; } = null!;
}

public static class ProjectTaskStatuses
{
    public const string ToDo = "ToDo";
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public static readonly IReadOnlyList<string> All = [ToDo, InProgress, Completed];
}

public static class ProjectTaskPriorities
{
    public const string Low = "Low";
    public const string Medium = "Medium";
    public const string High = "High";
    public static readonly IReadOnlyList<string> All = [Low, Medium, High];
}
