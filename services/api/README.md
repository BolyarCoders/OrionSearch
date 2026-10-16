# OrionSearch API

ASP.NET Core backend with the ORION-9 HTTP foundation and ORION-10 PostgreSQL persistence. The web client and Python research worker remain independent services.

## Requirements and local setup

Install .NET SDK **10.0.204**. `global.json` permits later patches in the 10.0.2xx feature band, but no prerelease SDKs. Run these commands from `services/api/`:

```sh
dotnet restore
dotnet tool restore
dotnet build
dotnet test
dotnet run --project src/OrionSearch.Api
```

The checked-in launch profile selects Development and listens on `http://localhost:5190`. Stop the API with Ctrl+C. Starting the API and calling `/health` requires no external service. The full test suite requires Docker or a local PostgreSQL server; see below. Railway is not needed.

| Endpoint | Response |
| --- | --- |
| `GET /health` | `200` with `{"status":"healthy"}`; process liveness only |
| `GET /health/ready` | `200 Healthy` when PostgreSQL accepts a connection; otherwise `503 Unhealthy` |
| `GET /openapi/v1.json` | OpenAPI JSON in Development; `404` in other environments |

There is no Swagger UI. The `.http` file beside `Program.cs` contains sample requests. Unknown routes return ProblemDetails; unexpected exceptions return a generic `500` ProblemDetails response with a `traceId`. Exception details stay in the server logs.

Readiness checks connectivity with a five-second check timeout. It does not apply migrations or certify schema compatibility. Health-check responses are plain text and reveal no connection details. The liveness and readiness endpoints are public; restrict their network exposure in deployment if needed.

## Structure

```text
OrionSearch.sln
global.json
dotnet-tools.json
src/
  OrionSearch.Api/
    Configuration/
    Endpoints/
    Extensions/
    Properties/launchSettings.json
    Program.cs
    appsettings.json
    appsettings.Development.json
  OrionSearch.Application/
    Abstractions/ICurrentUser.cs
  OrionSearch.Domain/
    Auth/
    Content/
    Jobs/
    Knowledge/
    Research/
  OrionSearch.Infrastructure/
    DependencyInjection.cs
    Persistence/
      OrionSearchDbContext.cs
      OrionSearchDbContextFactory.cs
      DatabaseReadinessCheck.cs
      Configurations/
      Migrations/
tests/
  OrionSearch.Tests/
    Persistence/
docs/database.md
```

Api owns HTTP handling and startup. References flow from Api to Application and Infrastructure, from Infrastructure to Application, and from Application to Domain. Domain contains plain entities and normalization helpers with no EF Core, Npgsql or ASP.NET Core dependencies. Infrastructure owns EF mappings, migrations, context registration and database readiness. No generic repository or second persistence abstraction is introduced before an application use case needs it.

`ICurrentUser` is the application boundary for a future authenticated identity. Its nullable string ID allows different identity providers. It has no implementation or DI registration yet. Authorization services and middleware are present, but authentication schemes and endpoint policies must be added when the team chooses an authentication provider. The health endpoint is public.

ASP.NET Core creates a scoped context for each request that needs persistence and disposes it with the scope. Integration tests use `WebApplicationFactory`; their failure and request probes are injected by the test assembly and aren't API endpoints. The Python worker remains a separate service; versioned JSON contracts will be agreed under `packages/contracts/` before integration.

## Configuration

ASP.NET Core loads `appsettings.json`, then `appsettings.{Environment}.json`, Development user secrets, environment variables and command-line arguments, in increasing precedence. Nested environment keys use double underscores. `.env` and `appsettings.Local.json` are **not** loaded automatically. Keep local secrets out of tracked settings; the API project has a user-secrets ID for development.

| Setting | Default / purpose |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | Production without a launch profile; the local profile selects Development |
| `ASPNETCORE_URLS` | Set listening addresses when running without a launch profile |
| `Cors__AllowedOrigins__0` (and subsequent indexes) | Exact frontend origins, with no paths or trailing slashes; none outside Development |
| `ReverseProxy__KnownProxies__0` (and subsequent indexes) | Additional trusted proxy IP addresses; empty by default |
| `AllowedHosts` | `*` locally; restrict to deployment hostnames when deploying |
| `Logging__LogLevel__Default` | `Information`; console logs use JSON |
| `ConnectionStrings__OrionSearch` | PostgreSQL connection string; equivalent to `ConnectionStrings:OrionSearch` |

Development allows `http://localhost:3000`. CORS allows methods and headers from configured origins, without credentialed cookie requests. Invalid origins and proxy addresses stop startup with a configuration error. Arrays merge by index across configuration sources, so edit the environment settings file when replacing a list with a shorter one.

For example, to run with Production settings on another port in PowerShell:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ASPNETCORE_URLS = 'http://localhost:5191'
$env:Cors__AllowedOrigins__0 = 'https://web.example'
dotnet run --project src/OrionSearch.Api --no-launch-profile
```

Forwarded headers run before the rest of the pipeline. Only `X-Forwarded-For` and `X-Forwarded-Proto` are accepted, for one proxy hop, from ASP.NET Core's default loopback proxies or explicitly configured IPs. Other peers' forwarded headers are ignored. Before a Railway deployment, establish its proxy addresses, configure trust and hostnames, and bind to its assigned port. TLS termination belongs to that deployment's reverse proxy; local HTTP does not redirect to HTTPS. No Railway deployment or infrastructure is included here.

## Dependencies and tests

The API uses `Microsoft.AspNetCore.OpenApi` 10.0.8 and the ASP.NET Core shared framework. `Microsoft.OpenApi` is explicitly pinned to 2.7.5 to avoid the vulnerable 2.0.0 transitive default ([upstream advisory](https://github.com/advisories/GHSA-v5pm-xwqc-g5wc)). Logging, CORS, options validation, authorization and ProblemDetails use built-in framework features.

Persistence uses EF Core, EF Core Relational and EF Core Design **10.0.13**, Npgsql EF provider **10.0.3**, and health-check services **10.0.13**. The local `dotnet-ef` tool is pinned to **10.0.13**. Relational is pinned explicitly to keep the provider and tooling dependencies on the same EF patch version.

Tests use xUnit 2.9.3, its Visual Studio runner 3.1.4, `Microsoft.NET.Test.Sdk` 17.14.1, `Microsoft.AspNetCore.Mvc.Testing` 10.0.8 and `Testcontainers.PostgreSql` 4.16.0. The original API tests still run without a database. The PostgreSQL tests exercise migrations, defaults, relationships, uniqueness, CHECK constraints, evidence provenance, deletion restrictions, full-text search, readiness and concurrent `SKIP LOCKED` selection. Data tests roll back their transactions; migration and concurrency tests use disposable databases.

```sh
# API and normalization tests; no PostgreSQL or Docker required
dotnet test --filter 'Category!=PostgreSQL'

# Actual PostgreSQL tests; Docker starts a postgres:18-alpine container by default
dotnet test --filter 'Category=PostgreSQL'

# Full suite and formatting
dotnet test
dotnet format OrionSearch.sln --verify-no-changes
```

If Docker is unavailable, set `ORIONSEARCH_TEST_POSTGRES` to an admin connection on a local test server, then run `dotnet test`. The test role must be able to create databases and install `citext`. The fixture creates random `orion_tests_<uuid>` databases and deletes only those databases afterward. It never migrates, truncates or drops the database named in the admin connection. Use a local test server rather than a production server. Without Docker or this setting the PostgreSQL tests fail; they are not silently skipped.

## PostgreSQL and migrations

The schema targets **PostgreSQL 16 or later** and has been tested locally on **18.6**. PostgreSQL must have `citext` available. Use an existing local PostgreSQL installation or the team's eventual infrastructure setup; this task adds no Docker Compose stack.

Create a local development database and a role with schema/extension creation rights. Set `ConnectionStrings:OrionSearch` via Development user secrets for API use, or set the environment variable for both the API and migration CLI. For example, with a locally authenticated role:

```powershell
$env:ConnectionStrings__OrionSearch = 'Host=localhost;Port=5432;Database=orionsearch;Username=YOUR_LOCAL_ROLE'
dotnet ef database update --project src/OrionSearch.Infrastructure
dotnet run --project src/OrionSearch.Api
```

Supply any required password through your local secret mechanism. Do not put actual credentials in tracked settings or command examples. The design-time factory reads `ConnectionStrings__OrionSearch` directly and does not load API user secrets. Model generation works without a database connection; applying migrations requires one.

```sh
dotnet ef migrations add DescribeSchemaChange --project src/OrionSearch.Infrastructure --output-dir Persistence/Migrations
dotnet ef migrations script --idempotent --project src/OrionSearch.Infrastructure --output migration-review.sql
dotnet ef migrations has-pending-model-changes --project src/OrionSearch.Infrastructure
```

Review generated code and SQL before applying it. Keep local review SQL outside the repository or remove it before committing. The initial chain is:

1. `CreateUsersAndSchemas` — `citext`, all seven schemas and users.
2. `CreateArchivesAndMembership` — archives, settings and membership.
3. `CreateResearchHistory` — runs and queries.
4. `CreateContentStorage` — publishers, global documents, versions, archive links and chunks.
5. `CreateFindingsAndEvidence` — findings and version-bound evidence.
6. `CreateJobQueue` — jobs, attempts, queue indexes and database-maintained `updated_at` triggers.

Migration history lives in `system.__ef_migrations_history`, separate from the 15 product tables. `ai` and `publishing` remain empty. The API does **not** run migrations during startup and never uses `EnsureCreated()`.

When Railway PostgreSQL is provisioned, use its provider-supplied credentials and TLS requirements through the same environment key. Review an idempotent migration script and apply it as an explicit deployment step with a migration role. Do not reuse the local test fixture against Railway. No Railway deployment has been performed.

See [database architecture](docs/database.md) for all 15 tables, constraint rationale, ownership, normalization assumptions and pending decisions.

## Later phases

ORION-11 implements archive REST operations, including an atomic transaction that creates an archive, its settings and its matching owner membership. ORION-12 adds research/job APIs and worker/agent integration. Job execution, scheduling, crawling, magazines and embeddings remain out of scope here.

The following configuration categories are future design decisions, not settings consumed by this API:

| Category | To define when implemented |
| --- | --- |
| Authentication | Provider, issuer, audience, schemes and user-context implementation |
| Research service | Endpoint, versioned contracts, authentication and HTTP timeouts |
| Research jobs | Scheduling, retry limits, cancellation and concurrency |

CI/CD, Docker Compose, Railway setup and frontend changes are outside this issue.
