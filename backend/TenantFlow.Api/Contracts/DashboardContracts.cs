namespace TenantFlow.Api.Contracts;

public sealed record DashboardSummaryResponse(
    int TotalProjects,
    int ActiveProjects,
    int CompletedProjects,
    int TotalTasks,
    int TodoTasks,
    int InProgressTasks,
    int CompletedTasks,
    int OverdueTasks,
    int TotalMembers,
    decimal TaskCompletionPercentage);
