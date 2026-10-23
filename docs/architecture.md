# Architecture direction

## Current implementation

- `backend/TenantFlow.Api` is a .NET 10 controller-based ASP.NET Core API with a sample weather forecast endpoint, HTTPS redirection, and development-only OpenAPI document generation.
- `frontend` is a React application using TypeScript and Vite. It contains starter content and has no API integration.
- There are no EF Core contexts, migrations, business modules, Identity stores, automated test projects, containers, or CI workflows.

## Modular monolith

Start with a single API deployment and separate business modules. This avoids the deployment and distributed transaction overhead of microservices while establishing clear ownership within the application.

When implementation begins, give each module its own project and organize its code into domain rules, application use cases, infrastructure adapters, and HTTP endpoints. The API host should compose dependencies and middleware rather than contain business rules. Do not introduce empty layers or a generic repository framework just to match a template.

The following boundaries are planned, not implemented:

| Module | Intended responsibility |
| --- | --- |
| Identity | Accounts, credentials, sign-in, and integration with ASP.NET Core Identity |
| Tenants | Tenant registration, tenant metadata, and lifecycle |
| Employees | Tenant-owned employee records and related use cases |
| Projects | Tenant-owned projects and related use cases |
| Subscriptions | Subscription plans, tenant subscriptions, and lifecycle |

Modules should expose intentional application contracts. Avoid direct access to another module's tables, EF entities, or internal services. Introduce shared code only when multiple modules have a concrete need; do not create a shared business-model dumping ground.

## Persistence and tenant boundaries

PostgreSQL with EF Core is the planned persistence stack. Decide the tenant storage strategy before introducing tenant-owned business data. Define module-owned contexts, table/schema ownership, explicit migrations, and cross-module transaction requirements as part of that work.

Tenant identification, authenticated membership, authorization, background processing, and database access must all participate in the isolation design. A tenant identifier or query filter alone does not establish complete isolation. Authentication and tenant isolation are not implemented in Phase 1.

## Application conventions for future work

- Use dependency injection at module boundaries and async I/O with cancellation support.
- Validate requests and enforce business invariants in the domain/application code.
- Return consistent HTTP errors without exposing internal exception details.
- Keep database credentials and signing material outside the repository.
- Add domain tests and HTTP/persistence integration tests with the corresponding features.
- Keep migrations explicit and reviewable; decide migration execution separately from ordinary API startup.

## Delivery

Docker and Compose will provide local dependencies. GitHub Actions will build, lint, and test once workflows are introduced. Kubernetes manifests and deployment packaging are deferred until the application has a concrete deployment requirement. This phase creates no services or paid infrastructure.
