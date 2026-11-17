# PostgreSQL integration tests

This project tests the correct outer `../TenantFlow.Api/TenantFlow.Api.csproj`
application through ASP.NET Core's real HTTP pipeline. It uses xUnit,
Microsoft.NET.Test.Sdk, Microsoft.AspNetCore.Mvc.Testing, FluentAssertions 7.2.0
and Testcontainers.PostgreSql. Authentication, Identity password hashing,
UserManager, authorization, Npgsql, constraints and queries are real. There is
no authentication shim, HTTP mock, EF InMemory provider or external database fallback.

## Prerequisites and commands

Install the .NET 10 SDK and start Docker Desktop with its Linux engine (or a
compatible local Docker engine). `docker version` must show both client and
server. The first run downloads `postgres:17-alpine` and Testcontainers' resource
reaper image. Keep the reaper enabled. Docker's credential helper must be on
PATH if Docker is configured to use one. For a per-user Windows Docker Desktop
installation, this may be necessary in the test terminal:

```powershell
$env:Path = "$env:LOCALAPPDATA\Programs\DockerDesktop\resources\bin;" + $env:Path
```

From the repository root:

```powershell
dotnet restore .\backend\TenantFlow.slnx
dotnet build .\backend\TenantFlow.slnx --configuration Release -p:UseAppHost=false
dotnet test .\backend\TenantFlow.slnx --configuration Release --no-build --logger "console;verbosity=minimal"
```

Release output avoids overwriting the running development API's Debug output.
The backend solution includes only the outer API and this new test project;
the accidentally nested API and its solution are untouched. Tests use TestServer,
so neither a running development API nor an HTTPS development certificate is needed.

Run just the safety smoke test or a feature group:

```powershell
dotnet test .\backend\TenantFlow.Api.Tests --configuration Release --no-build --filter FullyQualifiedName~DatabaseSafetyTests
dotnet test .\backend\TenantFlow.Api.Tests --configuration Release --no-build --filter FullyQualifiedName~TaskTests
```

Frontend validation uses the existing Vitest and React Testing Library setup:

```powershell
cd frontend
npm ci
npm run build
npm run test -- --run
```

Use a Node version compatible with the existing Vite project (the checked setup
uses Node 24). HTTP adapters and sample records live only in frontend test files;
no functional page imports them.

## Database and secret safety

- One new, uniquely named PostgreSQL container is created per xUnit collection run.
  No pre-existing PostgreSQL server, connection string or database is accepted.
- Every test gets a separate `tf_case_<random GUID>` database. Tests are grouped
  in one xUnit collection so shared container bookkeeping stays sequential.
- The fixture generates the container password, user passwords, signing key,
  emails and tenant slugs at runtime. Credentials are not written to files,
  host environment variables, assertions or application logs. Test account objects
  do not have a generated record ToString that could print a password.
- The test host starts in `Testing`. Generated settings are supplied before the
  minimal Program reads JWT configuration, and later configuration providers are
  cleared. User secrets, appsettings connection strings and ambient connection
  string variables cannot select a database. A startup check verifies Testing
  mode and the private signing key before any API request or migration.
- Before migrations, the actual DbContext connection is checked against the
  fixture's private database registry, expected name pattern, container host,
  random port and generated credentials. The same check precedes database removal.
- Existing migrations run only against that fresh, verified database. No migration
  source is changed and development/production migrations are never applied.
- Clients and the application host are disposed before the test database is
  dropped. The container and its resources are disposed at collection teardown,
  with Testcontainers' reaper covering interrupted runs. Cleanup never invokes
  broad Docker prune or operates on an externally supplied database name.
- A missing Docker engine is a test setup failure, not a skipped test or a switch
  to an unsafe database. No development or production data is needed by these tests.

## Coverage

| Test file | Behaviors |
| --- | --- |
| AuthenticationTests | Registration and initialized Identity fields, duplicate normalized email, real login/JWT access, invalid password/input, anonymous and corrupted-token rejection |
| TenantSecurityTests | Creation/selection, ownership, member/admin restrictions, Owner protection, invitations, conflicts and removal preserving account/other memberships |
| ProjectTests | Owner/Admin CRUD, Owner-only deletion, real FK cascade, validation and cross-tenant IDs/list isolation |
| TaskTests | CRUD, assignment/unassignment, rejected outside/removed assignees, Member own-status permissions, filtering/search/pagination and tenant/project isolation |
| DashboardTests | PostgreSQL aggregate translation, project/task/member counts, completion denominator/rounding, UTC overdue boundaries, empty data and tenant isolation |
| DatabaseSafetyTests | Real PostgreSQL provider, applied migration history, empty per-test database and rejected unowned database names |

The existing `backend/Tests/TenantFlow.Tests` lightweight suite is preserved,
but is not part of this solution's PostgreSQL integration run. Its historical
InMemory/authentication-fixture checks are not counted as real database coverage.

References: [Testcontainers PostgreSQL](https://dotnet.testcontainers.org/modules/postgres/),
[ASP.NET Core integration testing](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0).
