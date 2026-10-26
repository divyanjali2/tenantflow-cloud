namespace TenantFlow.Api.Authorization;

public static class TenantRoles
{
    public const string Owner = "Owner";
    public const string Admin = "Admin";
    public const string Member = "Member";

    public static readonly IReadOnlyList<string> All = [Owner, Admin, Member];
}
