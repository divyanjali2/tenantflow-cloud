# TenantFlow Cloud frontend

React, strict TypeScript, Vite, Tailwind CSS through `@tailwindcss/vite`, React
Router, Axios, and Lucide React. This is a local frontend for the existing outer
`backend/TenantFlow.Api/TenantFlow.Api.csproj` API; it is not a hosted Sites deployment.

## Run locally

Use a recent Node release compatible with the installed Vite version (Node 24
LTS works). If using nvm-windows, select the installed version first, for example
`nvm use 24.14.1`. Open two terminals from the repository root.

Backend terminal, using your existing Development configuration and secrets:

```powershell
dotnet dev-certs https --trust
dotnet run --project .\backend\TenantFlow.Api\TenantFlow.Api.csproj --configuration TaskManagement --launch-profile https
```

The HTTPS profile listens at `https://localhost:7102`. The separate build
configuration avoids overwriting the Debug executable if it is already locked;
stop any existing API using these ports before starting another copy. Configure
the database and JWT settings through the backend's existing local secrets.
No credentials belong in frontend source or Vite environment variables.

Frontend terminal:

```powershell
cd frontend
npm ci
npm run dev
```

Open `http://localhost:5173`. Sign in with an existing account. All six routes are
available: `/login`, `/dashboard`, `/projects`, `/tasks`, `/team`, `/settings`.
Projects and tasks are functional. A project opens its board at
`/projects/{projectId}/tasks`; `/tasks` opens the first available project with a
project selector. Team provides the workspace member directory and Owner-only
membership management. The existing dashboard
shows the actual workspace count, membership role and workspace creation date.

## API and HTTPS setup

Only existing API endpoints are called:

| Action | Endpoint |
| --- | --- |
| Sign in | `POST /api/auth/login` |
| Verify signed-in user | `GET /api/auth/me` |
| List accessible tenants | `GET /api/tenants/mine` |
| List/create projects | `GET/POST /api/tenants/{tenantId}/projects` |
| Edit/delete project | `PUT/DELETE /api/tenants/{tenantId}/projects/{projectId}` |
| List/create tasks | `GET/POST /api/tenants/{tenantId}/projects/{projectId}/tasks` |
| Edit/delete task | `PUT/DELETE /api/tenants/{tenantId}/projects/{projectId}/tasks/{taskId}` |
| Change task status | `PATCH /api/tenants/{tenantId}/projects/{projectId}/tasks/{taskId}/status` |
| Assign/unassign task | `PATCH /api/tenants/{tenantId}/projects/{projectId}/tasks/{taskId}/assignee` |
| Load assignees (Owner/Admin) | `GET /api/tenants/{tenantId}/members` |
| Add registered user (Owner) | `POST /api/tenants/{tenantId}/invitations` |
| Change member role (Owner) | `PATCH /api/tenants/{tenantId}/members/{userId}/role` |
| Remove membership (Owner) | `DELETE /api/tenants/{tenantId}/members/{userId}` |
| Dashboard statistics (all tenant members) | `GET /api/tenants/{tenantId}/dashboard/summary` |

The browser calls relative `/api` URLs. Vite proxies them to the HTTPS backend
with `changeOrigin: true` and `secure: false` for the self-signed localhost
development certificate. This TLS verification exception exists only in the
local Vite dev proxy; it must not be copied into a production reverse proxy.
`dotnet dev-certs https --trust` creates/trusts the development certificate for
Kestrel and browser access. The existing backend has HTTPS redirection and no
CORS middleware; no CORS change is needed because browser requests stay on the
frontend origin. Calling the API directly from another browser origin would
require a narrowly scoped backend CORS policy, which this foundation does not add.

If the proxy cannot connect, first start the outer API with the HTTPS profile
and check its startup output for port 7102. Check the development certificate
with `dotnet dev-certs https --check`. A read-only check outside the restricted
shell confirmed a valid localhost development certificate. The HTTPS backend
and Vite's `/api/auth/me` proxy returned the expected unauthenticated 401.
No certificate trust or database configuration was changed by this implementation.

`npm run preview` previews only the production static bundle. It does not use
the development API proxy. For a deployment, serve the frontend and `/api`
under the same trusted HTTPS origin with a real reverse proxy, and configure
SPA fallback to `index.html` for frontend routes. No deployment is included.

## Session behavior

The backend issues JWT bearer tokens and has no cookie-session, refresh-token,
or server logout endpoint. Tokens stay exclusively in JavaScript memory, never
in localStorage, sessionStorage, URLs or logs. Refreshing/closing the page ends
the browser session and requires another sign-in. Cookies cannot be made
HttpOnly from React; adopting that session model needs a coordinated backend
change in a later phase.

Axios attaches the current bearer token to authenticated requests. A current
authenticated 401 clears the session and redirects protected routes to login;
the login endpoint's invalid-credentials 401 remains an inline form error.
Late 401 responses from an old token cannot end a newer session. Expiry also
clears the session using the server's `expiresIn` value. Protected route return
paths are restricted to known internal pages.

Logout removes the in-memory token and unmounts tenant data. It does not revoke
an already issued stateless JWT on the backend; that token expires normally.
Tenant selection is also memory-only and is restricted to `/tenants/mine`
results. Role labels are UI information, never authorization decisions; the
backend remains the authority. In-memory storage does not eliminate XSS risk.

## Structure and validation

- `src/api/`: typed backend contracts, Axios client/interceptors, login and `/me`.
- `src/auth/`: memory-only session, expiry and React subscription.
- `src/tenants/`: tenant loading, retries, selection and scoped context.
- `src/components/`: responsive shell, navigation, brand, selector and shared states.
- `src/pages/`: login, dashboard, projects, task boards, account details and the team directory.
- `src/api/team.ts`: typed member mutations, reuse of the existing member list, and context-specific errors.
- `src/components/MemberDialogs.tsx`: Add Member, role-change and removal confirmation dialogs.
- `src/api/work.ts`: project/task/member DTOs and tenant-scoped requests.
- `src/hooks/`: abortable resource and mutation state with late-response guards.
- `src/components/WorkScope.tsx`: resets tenant/role-specific pages and dialogs immediately.
- `src/components/ProjectForm.tsx`, `TaskForm.tsx`, `TaskCard.tsx`, `Modal.tsx`: CRUD, assignment, status controls and native accessible dialogs.
- `src/index.css`: Tailwind import, light theme, reusable component styles and responsive layout.
- `tests/auth.test.tsx`: route protection, login, tenant switching, errors, logout and expiry.
- `tests/work.test.tsx`: CRUD request contracts, roles, pagination/filtering and tenant isolation.
- `tests/team.test.tsx`: team roles, exact request bodies, search, errors and tenant switching.

```powershell
npm run build
npm test
```

The build typechecks application code, configuration and tests. Test-only Axios
adapters exercise the existing contracts without credentials or production data;
no functional page imports mock responses. A successful production build does
not prove live backend connectivity. Real sign-in requires your running API,
database, certificate and existing user credentials.

## Project and task behavior

All tenant members can view projects and tasks. Owner/Admin can create/edit
projects; only Owner can delete a project, after confirmation that its tasks
will also be deleted. Owner/Admin can create/edit/delete/assign tasks. Members
can change only their own assigned tasks' status. These controls are UI hints;
all authorization is still enforced by the existing backend.

The member-list API is restricted to Owner/Admin, so Members do not request it.
When an assignee email is unavailable, cards show “You” or a shortened user ID
with the full ID in a tooltip. Assignee loading errors do not hide the board;
assignment is disabled until the list loads, with a retry action. Existing
assignments are preserved in edit forms when the member list is unavailable.

Search is applied with the Search button or Enter. Status and priority filters
apply immediately. Filters combine on the server and reset to page 1. The board
groups the current paginated result into To Do, In Progress and Completed;
column counts describe that page, while the total comes from `totalCount`.
Pagination supports 10/20/50/100 tasks per page. Mutations reload the current
filtered page and deletion corrects a now-out-of-range page.

Due date/time forms use local time and send UTC ISO timestamps. An unchanged
due date preserves the original timestamp's seconds/precision. Project/task
request bodies include only mutable DTO fields, never tenant, project or creator
IDs. Tenant IDs always come from the selected tenant context. Switching tenant
or role unmounts forms, filters, lists and member options; pending requests are
aborted and late responses cannot replace the new workspace's state. A project
ID remaining in the URL must belong to the new tenant's fetched project list
before the frontend requests its tasks.

## Manual checks for Phase 4 Step 2

1. Sign in, select a workspace, and open Projects. Check description, status and
   creation date against its API response. As Owner, create/edit a project; open
   and cancel its delete confirmation. Confirm deletion on a disposable project
   and verify that the list refreshes.
2. Repeat with Admin and Member memberships. Admin can create/edit but has no
   project delete action. Member has viewing access without project write controls.
3. Open a project board. Create a task with a description, priority, assignee and
   local due date. Edit it, then verify UTC values and preserved creator/route IDs
   in browser Network requests. Confirm deletion only on a disposable task.
4. Change a task's status and assignee directly on its card; unassign it. Check
   the dedicated PATCH routes and minimal request bodies. As Member, only an own
   assigned task has a status control; assignment/edit/delete controls are absent.
5. Search task titles, combine status/priority filters, change page size and use
   Next/Previous. Check that counts reflect the current filtered server response.
6. Switch tenants while a list is loading or after applying board filters. No
   previous records, draft forms or member options should remain. On an old
   project URL, select a project in the new workspace; Network must never pair
   that workspace with the previous project's ID.
7. Check keyboard Tab/Shift+Tab/Escape in dialogs and narrow-screen layout.
   Simulate offline/400/403/404 responses and verify feedback/retry. A current
   authenticated 401 must return to login and clear all workspace data.

Automated tests use test-only API adapters; real authenticated CRUD still needs
your running backend and existing account. This step changes no backend files
and creates or applies no migrations.

## Team management — Phase 4 Step 3

Owners and Admins can view the team table at `/team`. It shows the actual email,
user ID, tenant role and permitted actions from the existing member response DTO.
That DTO has no name field, so search is a case-insensitive email substring match
over the currently loaded tenant's members. The table scrolls horizontally within
its container on small screens and is keyboard focusable.

Only Owners see Add Member, Change role and Remove controls. All Owner rows and
the authenticated user's own row are protected. Admins have read-only directory
access. Members see an access-limited message and do not request the member-list
endpoint, matching the backend's Owner/Admin restriction. Backend authorization
remains authoritative, including when cached frontend role information is stale.

Add Member accepts an existing registered email and `Admin` or `Member` role.
It sends exactly `{ email, role }` to the existing `/invitations` endpoint, which
immediately adds a membership; no email is sent and no account is created. Role
changes send exactly `{ role }`. Removal requires confirmation and deletes only
the tenant membership, preserving the user account and other memberships.

Validation errors (400), expiry (401), forbidden access (403), unknown users or
unavailable memberships/workspaces (404), duplicate memberships (409), and network
failures have visible feedback. Failed writes keep their dialog and draft values.
Successful writes reload the server's member list. Tenant/role changes remount the
page through the existing WorkScope, clearing search, records, dialogs and notices;
aborted or late responses cannot replace the new tenant's state.

Manual testing, with your existing backend and registered accounts:

1. Run `npm run build`, `npm test`, then `npm run dev` in `frontend/`.
2. As Owner, open Team and compare emails, IDs and roles to the member response.
   Search for part of an email using mixed case; clear the search. Check a narrow
   screen and use keyboard Tab/Shift+Tab/Escape with the table and dialogs.
3. Open Add Member. Verify its immediate-access explanation, add a registered user
   as Member, then change that user's role to Admin and back. No Owner option
   should exist. Verify minimal bodies and selected tenant IDs in Network requests.
4. Try an unknown registered email and an already-added email. Expect 404 and 409
   feedback respectively, with the entered values preserved. Simulate invalid
   role/email data, stale permissions and offline errors to check 400/403/network
   handling. An authenticated 401 must clear the session and return to login.
5. Open removal confirmation and cancel; no DELETE should occur. Then remove a
   disposable membership and verify that only workspace access is removed. Every
   Owner row must stay protected, including a different Owner's row.
6. As Admin, check that the directory is visible with no management controls. As
   Member, check that no member directory request or management control appears.
7. Switch workspaces after searching or during loading. Search, records and draft
   dialogs must reset; late responses must not display the previous workspace's
   members. All subsequent member requests must use the newly selected tenant.

Automated checks use isolated test-only API adapters; no sample data is supplied
to application pages and no real users or memberships are changed by the tests.
No backend files, migrations, or authentication configuration change in this step.

## Dashboard analytics — Phase 4 Step 4

The existing dashboard layout, tenant selector and workspace/account cards remain.
Its analytics section requests the selected tenant's dashboard summary. Project,
task, overdue and member counts and the completion percentage all come from that
API response. No statistics are fabricated when the request fails: the section
shows an error and retry action. Refresh statistics reloads the summary. Loading
skeletons replace old values, and tenant changes remount the analytics section,
abort requests and reject late responses from the previous workspace.

Analytics is loaded separately so other routes do not download chart code.
Recharts renders a task status donut and project status bars, with visible text
values alongside each chart. A native, labelled progress indicator presents task
completion. Empty workspaces render actual zero counts and empty chart states,
without fake slices. The summary supplies Active and Completed project counts;
the bar chart's Other value is `totalProjects - activeProjects - completedProjects`
and is explicitly labelled as Planning, On Hold and Cancelled combined.

The API is JWT-protected and available to Owner/Admin/Member memberships. A valid
token without membership, or a nonexistent tenant, receives 404. Read queries use
AsNoTracking and perform project/task aggregates in the database plus a scoped
membership count; entities are not loaded to calculate totals. Completed tasks
never count as overdue. Other tasks are overdue only if DueDateUtc is non-null
and strictly before the request's UTC time. The percentage is completed / total
times 100, rounded to two decimals; a zero task total returns 0. Responses are
marked no-store. No model or migration change is needed.

From the repository root:

```powershell
dotnet build .\backend\TenantFlow.Api\TenantFlow.Api.csproj --no-restore --configuration DashboardAnalytics
dotnet test .\backend\Tests\TenantFlow.Tests\TenantFlow.Tests.csproj --no-restore -p:OutputPath=bin/dashboard-analytics/ -p:UseAppHost=false
dotnet run --project .\backend\TenantFlow.Api\TenantFlow.Api.csproj --configuration DashboardAnalytics --launch-profile https
```

In a separate terminal:

```powershell
cd frontend
npm ci
npm run build
npm test
npm run dev
```

Stop an older API on the same ports before starting the new version. Verify the
dashboard with your own registered account and existing data. Compare summary
responses to the cards, charts and progress indicator; refresh after a task status
change. Switch between tenants with different counts while a request is pending
and verify that no previous statistics remain. Check an empty tenant, an overdue
incomplete task, a completed task with a past due date, and offline/error states.

The original Phase 4 backend checks remain in the legacy test directory. Phase 5
adds [real PostgreSQL integration tests](../backend/TenantFlow.Api.Tests/README.md)
with actual registration/login/JWT authentication and per-test container databases.
Migrations run only against those disposable test databases. Frontend tests
use isolated API adapters; functional dashboard pages always call the real API.
Live PostgreSQL/authenticated browser verification requires your local API and
account. New files include DashboardController/DashboardContracts, backend summary
tests, `src/api/dashboard.ts`, `src/components/DashboardAnalytics.tsx` and frontend
dashboard tests. Program registers TimeProvider.System; the shared backend factory
is extensible for JWT tests. The dashboard, styles and existing auth test adapters
are updated to support summary loading.

References: [Recharts responsive charts](https://recharts.github.io/en-US/api/ResponsiveContainer/),
[Tailwind Vite plugin](https://tailwindcss.com/docs/installation/using-vite),
[Vite proxy configuration](https://vite.dev/config/server-options.html#server-proxy),
[Axios interceptors](https://axios-http.com/docs/interceptors).
