# OrionSearch API

ASP.NET Core backend with the ORION-9 HTTP foundation, ORION-10 PostgreSQL persistence and ORION-11 archive management. The web client and Python research worker remain independent services.

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

`ICurrentUser` exposes a nullable internal UUID. `TrustedCurrentUser` reads exactly one `orionsearch:user_id` claim from an authenticated identity. Archive services verify that the corresponding persisted user exists and is active, then check persisted membership. The registered `Unconfigured` authentication scheme authenticates nobody. Health endpoints remain public. See authentication integration below before connecting the frontend.

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

Forwarded headers run before the rest of the pipeline. Only `X-Forwarded-For` and `X-Forwarded-Proto` are accepted, for one proxy hop, from ASP.NET Core's default loopback proxies or explicitly configured IPs. Other peers' forwarded headers are ignored. Before a Railway API deployment, establish its proxy addresses, configure trust and hostnames, and bind to its assigned port. TLS termination belongs to that deployment's reverse proxy; local HTTP does not redirect to HTTPS. Public API deployment is outside ORION-11.

## Archive API

All six routes require a trusted authenticated, active application user. HTTP DTOs reject unknown fields, including `ownerId` and `userId`; EF entities are never response contracts.

| Method and route | Permission / result |
| --- | --- |
| `POST /archives` | Active user; creates archive, settings and matching owner membership in one transaction; `201` with `Location` |
| `GET /archives` | Lists only the caller's consistent memberships; paginated summaries with source counts |
| `GET /archives/{id}` | Owner, editor or viewer; metadata, role and settings |
| `PATCH /archives/{id}` | Owner/editor for metadata and settings; owner only for any supplied status |
| `DELETE /archives/{id}` | Owner only; sets `archived`, returns `204` |
| `GET /archives/{id}/sources` | Owner, editor or viewer; paginated source summaries |

Detail/create/update responses have the shape `{ "archive": { "id", "title", "description", "status", "role", "createdAt", "updatedAt", "sourceCount" }, "settings": { "dateFrom", "dateTo", "languages", "preferredDomains", "excludedDomains", "config", "updatedAt" } }` (field names shown schematically). Lists use `{ "items": [], "pageNumber": 1, "pageSize": 20, "totalCount": 0 }`.

Requests without a valid internal identity, unknown users and disabled users receive `401`. Nonmembers and unknown archives both receive `404`. Members without permission receive `403`. Invalid inputs return `400` ProblemDetails; ownership/membership disagreement or missing settings returns `409`. Errors include `traceId`, and validation errors include field messages where available. Invalid query syntax also returns ProblemDetails.

Creation accepts `title`, optional `description` and optional `settings`. Title is trimmed, nonblank and at most 200 characters; description is at most 10000 characters. Dates are nullable ISO dates with `dateFrom <= dateTo`. Language codes use two-letter ISO 639-1 codes recognized by .NET's neutral cultures; whitespace/case is normalized. Domains use the existing DNS/IDNA normalizer, including lowercase ASCII and trailing-dot removal; URLs, ports and paths are invalid. Each array allows at most 100 entries. Duplicates after normalization and preferred/excluded overlap are errors. Omitted arrays default to `[]`, config defaults to `{}`, and explicit null arrays/config/settings are invalid. Config is an opaque JSON object of at most 16 KiB, within the HTTP JSON depth limit of 64. It stores preferences without starting research or defining the future worker contract.

PATCH merges supplied settings with the stored configuration and validates the result. Omitted properties stay unchanged; explicit null clears description, dateFrom or dateTo. Null title, status, settings, arrays and config are invalid. Supplied arrays replace the array; supplied config replaces the whole object. An empty PATCH is a no-op for an authorized editor/owner. Archive/settings writes share a transaction, with the archive row locked before settings are read so concurrent patches validate against committed state. Database-maintained timestamps are returned for each changed record.

DELETE never physically deletes anything. It retains memberships, settings, runs, jobs, archive-document links, versions, chunks, findings and evidence. Repeating DELETE leaves the archived state intact without changing its timestamp. An owner can unarchive with `PATCH { "status": "active" }`; `paused` is also supported. Archived archives remain readable to members.

Archive list parameters: `page=1` (1–1000000), `pageSize=20` (1–100), optional `status=active|paused|archived`, `includeArchived=false`. An explicit status takes precedence over the archived default. Sorting is `createdAt DESC, id ASC`. Inconsistent ownership memberships are excluded from lists and fail closed on direct access.

Source parameters: the same pagination bounds, `publishedFrom`, `publishedTo`, `language`, `publisherDomain` and `sort`. Dates are inclusive UTC publication dates; missing publication dates remain null and are excluded when a date bound is supplied. Supported sorts are `added_desc` (default), `added_asc`, `published_desc`, `published_asc`, `relevance_desc`, `title_asc`. Null publication/title/relevance values sort last; document UUID breaks all ties. Queries join archive links, documents and optional publishers, count/filter/page in SQL, and project only summary columns. There is no per-document query. A discovered document needs no version to appear; a shared document can appear in each authorized archive. Full extracted text, raw storage paths and version bodies are omitted.

### Authentication integration still required

No identity provider has been agreed or configured. Normal startup returns `401` for archive routes in every environment, including Development. Supplying a bearer string, a public `X-User-Id` header or the test header cannot authenticate a request.

Before real frontend access, register the team's validated authentication scheme as the default authenticate/challenge scheme, configure its trust boundary (for example issuer/audience/signature validation for a token provider), and map its verified external subject to an **existing** `auth.users.id`. Only that server-side mapping may issue `orionsearch:user_id`. Do not copy an arbitrary `sub` claim into the internal UUID or auto-create users during archive requests. Membership roles always come from PostgreSQL, including when the principal carries role claims. Unknown, malformed, empty or ambiguous internal claims fail closed. Login, user provisioning and identity-provider selection remain separate work.

The test assembly alone registers an isolated authentication handler with `WebApplicationFactory`. Its header is never read by the API's production authentication handler. Local manual testing of authenticated operations requires the same real provider integration; there is no development-user shortcut. The `.http` examples are intended for use after that integration.

## Dependencies and tests

The API uses `Microsoft.AspNetCore.OpenApi` 10.0.8 and the ASP.NET Core shared framework. `Microsoft.OpenApi` is explicitly pinned to 2.7.5 to avoid the vulnerable 2.0.0 transitive default ([upstream advisory](https://github.com/advisories/GHSA-v5pm-xwqc-g5wc)). Logging, CORS, options validation, authorization and ProblemDetails use built-in framework features.

Persistence uses EF Core, EF Core Relational and EF Core Design **10.0.13**, Npgsql EF provider **10.0.3**, and health-check services **10.0.13**. The local `dotnet-ef` tool is pinned to **10.0.13**. Relational is pinned explicitly to keep the provider and tooling dependencies on the same EF patch version.

Tests use xUnit 2.9.3, its Visual Studio runner 3.1.4, `Microsoft.NET.Test.Sdk` 17.14.1, `Microsoft.AspNetCore.Mvc.Testing` 10.0.8 and `Testcontainers.PostgreSql` 4.16.0. The original API tests still run without a database. The PostgreSQL tests exercise migrations, defaults, relationships, uniqueness, CHECK constraints, evidence provenance, deletion restrictions, full-text search, readiness and concurrent `SKIP LOCKED` selection. Data tests roll back their transactions; migration and concurrency tests use disposable databases.

```sh
# API and normalization tests; no PostgreSQL or Docker required
dotnet test --filter 'Category!=PostgreSQL'

# Actual PostgreSQL tests; Docker starts a postgres:16-alpine container by default
dotnet test --filter 'Category=PostgreSQL'

# Full suite and formatting
dotnet test
dotnet format OrionSearch.sln --verify-no-changes
```

If Docker is unavailable, set `ORIONSEARCH_TEST_POSTGRES` to an admin connection on a local test server, then run `dotnet test`. The test role must be able to create databases and install `citext`. The fixture creates random `orion_tests_<uuid>` databases and deletes only those databases afterward. It never migrates, truncates or drops the database named in the admin connection. Use a local test server rather than a production server. Without Docker or this setting the PostgreSQL tests fail; they are not silently skipped.

With Docker available, select each major version in PowerShell:

```powershell
Remove-Item Env:ORIONSEARCH_TEST_POSTGRES -ErrorAction SilentlyContinue
$env:ORIONSEARCH_TEST_POSTGRES_IMAGE = 'postgres:16-alpine'
dotnet test --filter 'Category=PostgreSQL'
$env:ORIONSEARCH_TEST_POSTGRES_IMAGE = 'postgres:18-alpine'
dotnet test --filter 'Category=PostgreSQL'
```

`ORIONSEARCH_TEST_POSTGRES` takes precedence over the image setting. For native servers, point it to a dedicated local PostgreSQL 16 server, run the suite, then repeat with the PostgreSQL 18 server. The default image is 16; `ORIONSEARCH_TEST_POSTGRES_IMAGE` overrides it only when Testcontainers is used.

## PostgreSQL and migrations

The schema targets **PostgreSQL 16 or later**. Final ORION-10 correction verification ran on isolated native Windows instances:

| PostgreSQL | Full suite | PostgreSQL tests | Result |
| --- | --- | --- | --- |
| 16.15 | 129 | 87 | Passed; zero failures or skips |
| 18.6 | 129 | 87 | Passed; zero failures or skips |

Both runs cover fresh initialization, upgrades from the original six migrations, preservation of versions/evidence, idempotent SQL, rollback/reapply, raw-only captures, and the original API regressions. Docker was unavailable, so Testcontainers startup itself was not executed. PostgreSQL must have `citext` available. Use an existing local PostgreSQL installation or the team's eventual infrastructure setup; this task adds no Docker Compose stack.

Create a local development database and a role with schema/extension creation rights. Set `ConnectionStrings:OrionSearch` via Development user secrets for API use, or set the environment variable for both the API and migration CLI. For example, with a locally authenticated role:

```powershell
$env:ConnectionStrings__OrionSearch = 'Host=localhost;Port=5432;Database=orionsearch;Username=YOUR_LOCAL_ROLE'
dotnet ef database update --project src/OrionSearch.Infrastructure
dotnet run --project src/OrionSearch.Api
```

Supply any required password through your local secret mechanism. Do not put actual credentials in tracked settings or command examples. The design-time factory uses environment variables and does not load API user secrets. Model generation works without a database connection; applying migrations requires one.

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
7. `AllowRawOnlyDocumentVersions` — nullable extracted text, captured-content presence and raw-hash provenance constraints; preserves existing versions and evidence.

A captured version can contain normalized extracted text, raw bytes referenced by a durable `storage_uri`, or both. A raw-only version hashes actual preserved bytes with SHA-256 and records `content_hash.algorithm` and `content_hash.basis` in metadata. Text hashing retains its existing normalization policy. A version with neither representation is rejected; a URL-only discovery belongs in `documents`. See [captured representations and hashes](docs/database.md#captured-representations-and-hashes) for the exact policy. Downgrading migration seven is blocked while raw-only versions exist, so rollback cannot silently fabricate text.

Migration history lives in `system.__ef_migrations_history`, separate from the 15 product tables. `ai` and `publishing` remain empty. The API does **not** run migrations during startup and never uses `EnsureCreated()`.

### Railway configuration and safe inspection

Runtime and design-time tooling share this precedence:

1. `ConnectionStrings:OrionSearch` / `ConnectionStrings__OrionSearch` as an Npgsql ADO.NET string. An explicitly empty value intentionally disables fallback.
2. `DATABASE_URL`, parsed as a `postgres://` or `postgresql://` URI into `NpgsqlConnectionStringBuilder` (including percent-encoded credentials). The optional `sslmode` supports disable, allow, prefer, require, verify-ca and verify-full. Other URI query parameters are rejected; use the explicit ADO.NET key for additional options.
3. Railway `PGHOST`, `PGPORT` (default 5432), `PGDATABASE`, `PGUSER`, `PGPASSWORD`, composed with `NpgsqlConnectionStringBuilder`.

For a future API in the same Railway environment, reference the existing PostgreSQL service variables and use its private hostname. Examples of variable references are `${{Postgres.DATABASE_URL}}` or `${{Postgres.PGHOST}}`; replace `Postgres` with the actual inspected service name. Keep secrets in Railway-managed variables or local secret storage. Follow the service's actual TLS requirements; the URI's sslmode or explicit Npgsql options control TLS. No certificate-validation bypass is introduced. Do not pass a PostgreSQL URI as `ConnectionStrings__OrionSearch`, print it in logs, or copy credentials into tracked files.

Before applying anything to Railway:

1. Inspect the connected account and confirm the OrionSearch project ID, environment and PostgreSQL service; a local link alone is insufficient.
2. Confirm a dedicated authorized development database. Reuse an appropriate service. Creating billable resources, plan changes and subscriptions need separate explicit approval.
3. Through an already authorized secure connection, run the read-only [inspection script](scripts/inspect-database.sql). For example, with libpq connection variables already supplied securely: `psql -X -v ON_ERROR_STOP=1 -f scripts/inspect-database.sql`. This reports version, schemas, extension, tables, migration history, row counts and constraints without returning application records. It handles an empty database without migration history. It is unrelated to the destructive local test fixture.
4. Stop before modifying a database with existing application data, unexpected tables or unknown migrations. Do not reset shared resources or change public networking to simplify access.
5. For a confirmed empty development destination, review all seven migrations using the idempotent script command above, then explicitly run `dotnet ef database update --project src/OrionSearch.Infrastructure` with the authorized connection supplied securely. Startup never runs migrations.
6. Re-run inspection: expect PostgreSQL 16+, all seven schemas, 15 product tables, seven known migrations and `citext`. Run the local API with the same secure connection and check `/health/ready`; `200` verifies connectivity, while schema compatibility comes from the inspection.

Use an existing Railway secure access method or tunnel if private networking prevents local access. Do not publish a database port automatically. Never set `ORIONSEARCH_TEST_POSTGRES` to Railway: that fixture creates and drops databases.

Railway verification for ORION-11 is pending: the installed integration was enabled and explicitly authorized, but this chat exposed no Railway operations or authenticated CLI. The browser fallback also opened at Railway login. No project/environment/service, hosted schema or migration state was inspected, and no hosted resource was provisioned or modified.

ORION-11 local validation ran on an isolated PostgreSQL 18.6 cluster: **223 tests passed**, with zero failures or skips, including all 129 original tests. Restore, tool restore, zero-warning build, formatting and the EF pending-model check passed. The read-only inspection script ran before and after applying the reviewed seven-migration SQL to a separate empty local database. Normal Production startup using `DATABASE_URL` returned healthy liveness/readiness and rejected unauthenticated archives. This verifies the local connection/migration workflow; it does not verify Railway networking, permissions or hosted migrations. Docker startup and a new PostgreSQL 16 run were not executed for ORION-11.

See [database architecture](docs/database.md) for all 15 tables, constraint rationale, ownership, normalization assumptions and pending decisions.

## Later phases

ORION-12 will add research/job APIs and worker/agent integration. Job execution, scheduling, crawling, magazines and embeddings remain out of scope here.

The following configuration categories are future design decisions, not settings consumed by this API:

| Category | To define when implemented |
| --- | --- |
| Authentication | Provider, trust configuration, external-subject mapping and user provisioning |
| Research service | Endpoint, versioned contracts, authentication and HTTP timeouts |
| Research jobs | Scheduling, retry limits, cancellation and concurrency |

CI/CD, Docker Compose, public API deployment and frontend changes are outside this issue. Authorized Railway database inspection/setup is included where the integration is available.
