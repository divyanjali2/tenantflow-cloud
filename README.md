# TenantFlow Cloud

TenantFlow Cloud is a planned multi-tenant SaaS business management platform. This monorepo currently contains an ASP.NET Core Web API starter and a React/TypeScript/Vite starter.

Phase 1 establishes the repository layout and documents development. Business modules, database integration, authentication, and tenant isolation are not implemented.

## Repository layout

```text
tenantflow-cloud/
├── backend/
│   ├── TenantFlow.slnx
│   └── TenantFlow.Api/       # Existing .NET 10 API starter
├── frontend/                # Existing React, TypeScript, and Vite starter
├── infrastructure/          # Infrastructure conventions; configuration planned
├── docs/
│   └── architecture.md      # Architecture direction and module responsibilities
├── .github/                 # GitHub contribution and automation conventions
├── .gitignore
└── README.md
```

## Architecture

The intended backend is a modular monolith: one deployable API with business capabilities separated into modules. Identity, Tenants, Employees, Projects, and Subscriptions are planned boundaries; they are not existing implementations. Module boundaries will be introduced when those capabilities are developed.

PostgreSQL and Entity Framework Core are planned for persistence, with ASP.NET Core Identity for account management. The React application will communicate with the API over HTTP. Docker will support local infrastructure, GitHub Actions will run automated checks, and Kubernetes deployment configuration will be considered later.

This keeps the initial application operationally simple while allowing modules to develop independently. See [architecture decisions and future boundaries](docs/architecture.md) and [infrastructure conventions](infrastructure/README.md).

## Prerequisites

- .NET 10 SDK.
- A Node.js release compatible with the Vite version in `frontend/package.json`, plus npm. The installed package's `engines` requirements are authoritative.
- Git.
- Docker with Compose will be needed once local infrastructure is introduced; it is not required for the current starter applications.

## Local development

Run commands from the repository root unless stated otherwise.

### Backend

```powershell
dotnet restore backend/TenantFlow.slnx
dotnet build backend/TenantFlow.slnx --no-restore
dotnet dev-certs https --trust
dotnet run --project backend/TenantFlow.Api --launch-profile https
```

The existing HTTPS launch profile listens at `https://localhost:7010` and `http://localhost:5102`; the API redirects HTTP requests to HTTPS. The development certificate command may prompt for operating system trust confirmation.

The starter endpoint is `GET https://localhost:7010/weatherforecast`. In Development, the generated OpenAPI document is available at `https://localhost:7010/openapi/v1.json`. There is no Swagger UI configured. No database or application secrets are needed for this starter.

### Frontend

In a separate terminal:

```powershell
cd frontend
npm ci
npm run dev
```

Open the URL printed by Vite. The frontend currently shows the starter page and does not call the backend. API URL configuration, development proxying, and CORS policy will be introduced with the first integration.

### Checks

```powershell
dotnet build backend/TenantFlow.slnx
cd frontend
npm run lint
npm run build
```

There are no automated test projects or frontend test scripts yet. A successful build should not be interpreted as a passing business test suite.

## Configuration and secrets

Keep credentials out of source control. Future backend connection strings and credentials should come from .NET user secrets for local development or environment variables. User secrets have not been initialized in this phase. Never put secrets in frontend configuration: values included in a browser bundle are public.

The root `.gitignore` excludes local environment files, private certificate/key files, dependency folders, and generated build output. Example environment files may be committed if they contain placeholders only. Keep package lockfiles committed for reproducible frontend installs.

Some backend `bin/` and `obj/` files were already tracked in the starter repository. Ignore rules prevent new files from being added but do not untrack existing files. They remain untouched in Phase 1 to preserve the existing files; a separate repository cleanup can remove them from Git tracking later.

## Scope and next steps

Phase 1 adds structure and documentation only. Future work includes module projects, PostgreSQL/EF Core configuration and migrations, ASP.NET Core Identity integration, tenant-aware authorization and data access, business endpoints, automated tests, local Compose services, and CI workflows.

Authentication, authorization, and tenant isolation require explicit design and verification before production use. No cloud infrastructure is provisioned or deployed by this setup.
