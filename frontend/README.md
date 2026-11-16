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
Projects, tasks, and team are explicit navigation placeholders for future UI;
they do not make CRUD requests or show fake records. The dashboard shows the
actual workspace count, selected membership role and workspace creation date.

## API and HTTPS setup

Only existing API endpoints are called:

| Action | Endpoint |
| --- | --- |
| Sign in | `POST /api/auth/login` |
| Verify signed-in user | `GET /api/auth/me` |
| List accessible tenants | `GET /api/tenants/mine` |

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
- `src/pages/`: login, dashboard, account details and future-feature placeholders.
- `src/index.css`: Tailwind import, light theme, reusable component styles and responsive layout.
- `tests/auth.test.tsx`: route protection, login, tenant switching, errors, logout and expiry.

```powershell
npm run build
npm test
```

The build typechecks application code, configuration and tests. Test-only Axios
adapters exercise the existing contracts without credentials or production data;
no functional page imports mock responses. A successful production build does
not prove live backend connectivity. Real sign-in requires your running API,
database, certificate and existing user credentials.

References: [Tailwind Vite plugin](https://tailwindcss.com/docs/installation/using-vite),
[Vite proxy configuration](https://vite.dev/config/server-options.html#server-proxy),
[Axios interceptors](https://axios-http.com/docs/interceptors).
