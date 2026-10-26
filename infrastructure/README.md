# Infrastructure

## Local PostgreSQL

The root-level `docker-compose.yml` starts PostgreSQL 17 for local development. It creates the `tenantflow_db` database and `tenantflow_user` user, binds PostgreSQL to `127.0.0.1:5432`, and persists database files in a named Docker volume. The password is supplied through the root `.env` file and is not committed.

From the repository root, create your local environment file from the example and replace its placeholder password:

```powershell
Copy-Item .env.example .env
```

Start PostgreSQL in the background:

```powershell
docker compose -f infrastructure/docker-compose.yml up -d
```

Verify the container and health check:

```powershell
docker compose -f infrastructure/docker-compose.yml ps
docker compose -f infrastructure/docker-compose.yml exec postgres pg_isready -U tenantflow_user -d tenantflow_db
```

The service should show as `healthy`, and `pg_isready` should report that the server is accepting connections. To inspect startup or health-check output, run:

```powershell
docker compose -f infrastructure/docker-compose.yml logs postgres
```

Stop the service without removing its persisted database:

```powershell
docker compose -f infrastructure/docker-compose.yml down
```

The named `postgres_data` volume remains across stops. Do not use `docker compose down -v` unless you intend to permanently delete the local database data.

The root `.gitignore` excludes the real `.env` file and allows the placeholder-only `.env.example` to be committed. Keep production credentials out of local development configuration.

## Future deployment configuration

Kubernetes manifests or Helm packaging can be added when deployment work begins. Do not use production credentials for local development.
