# Tenant project tasks

All task routes use `/api/tenants/{tenantId}/projects/{projectId}/tasks`.
POST creates a task, GET lists tasks, and GET/PUT/DELETE with `/{taskId}`
retrieve, replace or delete a task. POST returns 201 with a Location header;
PUT returns 200 and DELETE returns 204.

JWT authentication and a current tenant membership are required. Owners and
Admins can create, update, delete and assign tasks. Members can read tasks and
change the status of tasks currently assigned to themselves through the status
PATCH endpoint. Nonmembers receive 404; insufficient permissions return 403.
Every task lookup includes both route tenant and project IDs. The parent project
must also belong to the route tenant.

Create and update accept `title`, `description`, `status`, `priority`,
`assignedToUserId` and `dueDateUtc`. The server sets IDs, creator and timestamps.
Title is required, cannot be whitespace and is limited to 200 characters.
Status is exactly `ToDo`, `InProgress` or `Completed` (default `ToDo`). Priority
is exactly `Low`, `Medium` or `High` (default `Medium`). Invalid fields return
400 ValidationProblemDetails. PUT replaces all editable fields; omitted nullable
fields are cleared and omitted status/priority fields use their defaults.

Due dates accept ISO 8601 timestamps and are converted to UTC before persistence.
Assignees must have an existing membership row in the same tenant. The existing
schema has no inactive/pending membership flag; membership removal deletes the
row. Removed members cannot receive new assignments or access tasks, while old
assignments retain their user reference for history. Users referenced by tasks
cannot be deleted directly because both user foreign keys restrict deletion.
Deleting a project cascades to its tasks without affecting users or memberships.

## Assignment and status updates

`PATCH /api/tenants/{tenantId}/projects/{projectId}/tasks/{taskId}/assignee`
accepts `{ "assignedToUserId": "GUID" }` or `{ "assignedToUserId": null }` to
unassign. The field must be present. Only Owners and Admins can assign tasks,
and the selected user must have a current membership in the route tenant.

`PATCH /api/tenants/{tenantId}/projects/{projectId}/tasks/{taskId}/status`
accepts `{ "status": "InProgress" }`. Owners and Admins can update any task;
Members can update only tasks assigned to their authenticated JWT subject.
Reassignment or removal of membership revokes that Member's permission.

Both endpoints return 200 with the updated task, change `UpdatedAtUtc` and
preserve other fields. Invalid input returns 400 ValidationProblemDetails.
Extra tenant/user/creator IDs in a body cannot override route IDs or the JWT
subject. Missing tasks and tasks outside the route tenant/project return 404.

## Filtering and pagination

GET on the task collection accepts these optional query parameters:

| Parameter | Behavior |
| --- | --- |
| `status` | Exact `ToDo`, `InProgress` or `Completed` |
| `priority` | Exact `Low`, `Medium` or `High` |
| `assignedToUserId` | GUID identifying the assignee to filter by |
| `search` | Case-insensitive literal substring of the title; trimmed; blank ignored |
| `page` | Positive integer, default 1 |
| `pageSize` | Integer from 1 to 100, default 20 |

Filters combine with AND and always apply within the route tenant and project.
An unknown or outside-tenant assignee filter returns no matching tasks. Invalid
enum values, GUIDs or pagination values return 400 ValidationProblemDetails.

The collection response is now an object instead of the previous bare array:

```json
{ "items": [], "page": 1, "pageSize": 20, "totalCount": 0, "totalPages": 0 }
```

`totalCount` counts all matches before pagination. Ordering is `CreatedAtUtc`
descending, then `Id` ascending. Out-of-range pages return empty items and retain
their requested page number and the filtered totals. Read queries are untracked
and filtering/counting/paging execute in the database. Step 3 does not change
the entity model or require an additional migration.

## Local migration

`AddProjectTasks` only creates the ProjectTasks table and its indexes/foreign
keys. No existing table or data is altered by its Up method. Migration generation
and inspection do not apply it to a database. Its Down method drops ProjectTasks,
so rolling it back after use removes task data.

From the repository root, with your existing local Development configuration:

```powershell
$env:DOTNET_ENVIRONMENT = 'Development'
dotnet ef database update AddProjectTasks --project .\backend\TenantFlow.Api\TenantFlow.Api.csproj --startup-project .\backend\TenantFlow.Api\TenantFlow.Api.csproj --configuration Release
```

Review your local connection configuration before running that command. It has
not been run as part of this implementation.

## Verification

These commands use separate build output paths to avoid the executable held by
the running development API:

```powershell
dotnet build .\backend\TenantFlow.Api\TenantFlow.Api.csproj --no-restore --configuration TaskManagement
dotnet test .\backend\Tests\TenantFlow.Tests\TenantFlow.Tests.csproj --no-restore -p:OutputPath=bin/task-management/ -p:UseAppHost=false
```

To run the API using its existing local configuration from the repository root:

```powershell
dotnet run --project .\backend\TenantFlow.Api\TenantFlow.Api.csproj --configuration TaskManagement --launch-profile http
```

The existing test host uses EF InMemory and a test authentication handler. Tests
exercise HTTP CRUD, RBAC, validation, assignees, removal of membership, route/body
isolation, UTC dates, assignment/reassignment, Member status permissions, title
search, combined filters and stable pagination. A separate Npgsql model test checks foreign keys,
delete behavior, required indexes, defaults and PostgreSQL timestamp mapping;
these tests do not apply migrations to PostgreSQL.
