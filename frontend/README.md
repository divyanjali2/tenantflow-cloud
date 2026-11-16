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
project selector. Team remains a navigation placeholder. The existing dashboard
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
- `src/pages/`: login, dashboard, projects, task boards, account details and team placeholder.
- `src/api/work.ts`: project/task/member DTOs and tenant-scoped requests.
- `src/hooks/`: abortable resource and mutation state with late-response guards.
- `src/components/WorkScope.tsx`: resets tenant/role-specific pages and dialogs immediately.
- `src/components/ProjectForm.tsx`, `TaskForm.tsx`, `TaskCard.tsx`, `Modal.tsx`: CRUD, assignment, status controls and native accessible dialogs.
- `src/index.css`: Tailwind import, light theme, reusable component styles and responsive layout.
- `tests/auth.test.tsx`: route protection, login, tenant switching, errors, logout and expiry.
- `tests/work.test.tsx`: CRUD request contracts, roles, pagination/filtering and tenant isolation.

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

References: [Tailwind Vite plugin](https://tailwindcss.com/docs/installation/using-vite),
[Vite proxy configuration](https://vite.dev/config/server-options.html#server-proxy),
[Axios interceptors](https://axios-http.com/docs/interceptors).
