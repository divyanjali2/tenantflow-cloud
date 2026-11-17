namespace TenantFlow.Api.Models;

public static class ProjectStatuses
{
    public const string Planning = "Planning";
    public const string Active = "Active";
    public const string OnHold = "OnHold";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";

    public static readonly IReadOnlyList<string> All =
        [Planning, Active, OnHold, Completed, Cancelled];
}
