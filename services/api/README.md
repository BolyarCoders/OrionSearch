# OrionSearch API

ASP.NET Core backend with the ORION-9 HTTP foundation, ORION-10 PostgreSQL persistence, ORION-11 archive management and ORION-12 research execution. The web client and Python research worker remain independent services.

For complete endpoint examples and an explanation of the backend workflow, see the [API and Backend Guide](docs/api-and-backend-guide.md).

For the Render API/database configuration, free-plan expiry and deployment checks, see [Render hosting](docs/render-hosting.md).

For the proposed architecture, implementation order and acceptance criteria for the remaining product work, see the [Backend completion plan](docs/backend-completion-plan.md).

Native ASP.NET Core Identity accounts, activation settings and endpoint contracts are documented in [Native Identity](docs/native-identity.md). This feature is disabled until explicitly configured; no external identity provider is required.

**Foundation scope:** PR #6 provides the backend implementation for team integration. It is not an authenticated, end-to-end accepted product release. The [release-blocker register and integration/security acceptance plan](docs/release-readiness.md) tracks required CI, real authentication, the agreed agent contract, live integration and deployment verification. Those gates must remain open until their required evidence is reviewed; backend Jira completion does not close them.

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
    Archives/
    Research/
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

Creation accepts `title`, optional `description` and optional `settings`. Title is trimmed, nonblank and at most 200 characters; description is at most 10000 characters. Dates are nullable ISO dates with `dateFrom <= dateTo`. Language codes use two-letter ISO 639-1 codes recognized by .NET's neutral cultures; whitespace/case is normalized. Domains use the existing DNS/IDNA normalizer, including lowercase ASCII and trailing-dot removal; URLs, ports and paths are invalid. Each array allows at most 100 entries. Duplicates after normalization and preferred/excluded overlap are errors. Omitted arrays default to `[]`, config defaults to `{}`, and explicit null arrays/config/settings are invalid. Config is an opaque JSON object of at most 16 KiB, within the HTTP JSON depth limit of 64. It stores preferences; research starts only through the separate submission endpoint and snapshots those settings into its versioned request.

PATCH merges supplied settings with the stored configuration and validates the result. Omitted properties stay unchanged; explicit null clears description, dateFrom or dateTo. Null title, status, settings, arrays and config are invalid. Supplied arrays replace the array; supplied config replaces the whole object. An empty PATCH is a no-op for an authorized editor/owner. Archive/settings writes share a transaction, with the archive row locked before settings are read so concurrent patches validate against committed state. Database-maintained timestamps are returned for each changed record.

DELETE never physically deletes anything. It retains memberships, settings, runs, jobs, archive-document links, versions, chunks, findings and evidence. Repeating DELETE leaves the archived state intact without changing its timestamp. An owner can unarchive with `PATCH { "status": "active" }`; `paused` is also supported. Archived archives remain readable to members.

Archive list parameters: `page=1` (1–1000000), `pageSize=20` (1–100), optional `topic`, `status=active|paused|archived`, `includeArchived=false`. An explicit status takes precedence over the archived default. Sorting is `createdAt DESC, id ASC`. Inconsistent ownership memberships are excluded from lists and fail closed on direct access.

Source parameters: the same pagination bounds, `topic`, `publishedFrom`, `publishedTo`, `language`, `publisherDomain` and `sort`. Dates are inclusive UTC publication dates; missing publication dates remain null and are excluded when a date bound is supplied. Supported sorts are `added_desc` (default), `added_asc`, `published_desc`, `published_asc`, `relevance_desc`, `title_asc`. Null publication/title/relevance values sort last; document UUID breaks all ties. Queries join archive links, documents and optional publishers, count/filter/page in SQL, and project only summary columns. There is no per-document query. A discovered document needs no version to appear; a shared document can appear in each authorized archive. Full extracted text, raw storage paths and version bodies are omitted.

Both lists support optional `topic` as a case-insensitive literal substring: archive title or description for `/archives`, document title for `/archives/{id}/sources`. This is metadata text filtering, not semantic classification, word/token search or full-document search. Surrounding whitespace is trimmed; the resulting term must contain 1–200 characters with no control characters. Omit `topic` for no text filter; empty, blank or oversized terms return `400` with a `topic` validation error. `%`, `_`, `!` and backslashes are literal characters, not SQL pattern syntax. Parameterized PostgreSQL `ILIKE ... ESCAPE '!'` predicates run inside the caller's membership scope before counting and pagination. Existing status/archived/date/language/domain filters combine with topic using AND; null descriptions/titles do not match by themselves. Ordering is unchanged. For example: `GET /archives?topic=artificial%20intelligence&page=1&pageSize=20` or `GET /archives/{id}/sources?topic=climate&publishedFrom=2026-01-01&sort=published_desc`. No schema change or new search service is required.

### Authentication integration still required

No identity provider has been agreed or configured. Normal startup returns `401` for archive and research routes in every environment, including Development. Supplying a bearer string, a public `X-User-Id` header or the test header cannot authenticate a request.

Before real frontend access, register the team's validated authentication scheme as the default authenticate/challenge scheme, configure its trust boundary (for example issuer/audience/signature validation for a token provider), and map its verified external subject to an **existing** `auth.users.id`. Only that server-side mapping may issue `orionsearch:user_id`. Do not copy an arbitrary `sub` claim into the internal UUID or auto-create users during archive requests. Membership roles always come from PostgreSQL, including when the principal carries role claims. Unknown, malformed, empty or ambiguous internal claims fail closed. Login, user provisioning and identity-provider selection remain separate work.

The test assembly alone registers an isolated authentication handler with `WebApplicationFactory`. Its header is never read by the API's production authentication handler. Local manual testing of authenticated operations requires the same real provider integration; there is no development-user shortcut. The `.http` examples are intended for use after that integration.

## Research API and processing

`POST /archives/{archiveId}/research-jobs` accepts `{}` or `{ "objective": "Research this topic" }`. Owners and editors can submit against an active archive; viewers receive `403`. An omitted/null objective uses the archive title; an explicit objective must be nonblank and at most 2000 characters. The server snapshots the archive's settings and topic. Unknown request fields are rejected. A successful transaction inserts one queued run and one `research_plan` job before returning `202` with `Location: /research-jobs/{id}`. The request never waits for the agent.

The public `id` is **`research.research_runs.id`**, not the private `system.jobs.id`. `GET /research-jobs/{id}` permits owners, editors and viewers and returns status, UTC timestamps, committed query/source/version/finding counts, attempts, maximum attempts, next retry time and a sanitized error. Run states are `queued`, `running`, `completed`, `failed` and `cancelled`. Internal payloads, worker IDs, credentials and model settings are excluded. Authentication remains fail-closed: normal startup returns `401` until the real provider is integrated. Authenticated nonmembers and unknown IDs both receive `404`.

An optional `Idempotency-Key` header accepts 1–200 visible ASCII characters. Its SHA-256 namespace includes the archive ID. Submission locks the archive and checks a normalized objective fingerprint, with database uniqueness as the backstop. Concurrent matching retries return the original run; incompatible reuse returns `409`. An omitted objective stays the same request fingerprint even if the archive title later changes. Existing matching submissions can be retrieved after a lifecycle change; they do not create new work. Without a key, each submission creates a new run.

The independently callable `ResearchProcessor` uses fresh EF contexts for queue operations; its hosted adapter can later move to a separate .NET host. Processing is disabled by default. It also refuses to claim/recover jobs when the agent is unconfigured. Each process gets a random worker-instance ID. Short `FOR UPDATE SKIP LOCKED` transactions claim due work by priority and creation time, increment the attempt generation, create attempt history and commit the lease before HTTP execution. No database transaction stays open during the agent call.

Heartbeats renew the lease during the agent call. Before transactional completion or failure, the processor stops renewal and awaits any in-flight heartbeat. Ownership validates worker/run/archive IDs, processing state, attempt generation and lease validity after acquiring the exclusive job-row lock. That lock protects completion through ingestion, even when the original deadline passes; finalization checks identity/generation/status without expiring its own locked transaction. An expired or superseded worker that has not acquired valid completion ownership is rejected. Failure finalization follows the same ownership rule. Losing the identity/generation fence rolls the entire transaction back. Expired abandoned attempts are finalized as timed out and requeued with bounded exponential backoff, or marked failed when exhausted. Concurrent recovery workers cannot steal a locked completion. Timeout/network/temporary service failures retry; contract, authentication and persistence failures are permanent. A process shutdown leaves its unfinished lease for recovery.

Paused archives reject new submissions and do not start pending attempts until reactivated. An attempt paused between claim and dispatch is requeued with backoff and consumes that claimed attempt. Work already dispatched may finish while paused. Archived archives reject new work, cancel pending/recovered attempts and reject late result persistence. Archive status changes and result commits serialize on the archive row; history is retained.

All settings below use the `ResearchWorker` section, for example `ResearchWorker__Enabled` in environment variables. Defaults are safe for API-only startup; no settings contain credentials in source control.

| Setting | Default / meaning |
| --- | --- |
| `Enabled` | `false`; explicit opt-in to processing |
| `LeaseSeconds`, `HeartbeatSeconds` | `120`, `30`; lease 10–3600 seconds, heartbeat at most one third of lease |
| `PollSeconds` | `5`; idle/recovery polling, 1–300 seconds |
| `TimeoutSeconds` | `300`; total agent request including response body, 1–3600 seconds |
| `MaxAttempts` | `3`; snapshotted into new jobs, 1–10 |
| `RetryBaseSeconds`, `RetryMaxSeconds` | `10`, `300`; exponential backoff capped at maximum |
| `MaxResponseBytes` | `2097152`; streaming body limit, configurable 1 KiB–10 MiB |
| `AgentBaseAddress` | Unset; fixed server-selected endpoint, with `research` appended |
| `AgentApiKey` | Unset; bearer service credential from secret configuration, never logged |
| `AllowProvisionalContract` | `false`; explicit opt-in while ORION-8 is unfinished |

The HTTP client requires HTTPS, except loopback or Railway's private `.railway.internal` hostnames; URLs cannot include credentials, queries or fragments. Redirects are disabled. A complete enabled configuration still requires a real compatible agent service. No production fake exists.

ORION-8 was To Do with no contract/worker implementation on either remote branch when inspected on 2026-10-18. The version **`orion.research.provisional.v1`** is explicitly a proposal. See the [AI handoff, schemas and representative fixtures](../../packages/contracts/research/provisional-v1/README.md). The typed boundary rejects unknown/missing fields, unsupported versions, invalid counts and cross-source references. Agree the final contract, transport/authentication and deterministic chunking with the AI team before enabling live processing.

Result ingestion normalizes URLs/domains and upserts global publishers, documents, versions and archive links in deterministic order. Publication dates stay unknown when absent. URL-only discoveries create no captured version. Text hashes use the existing normalization helper; raw-only captures require a durable storage reference and supplied SHA-256 with explicit hash-basis metadata. The agent/storage integration must preserve the actual bytes; this backend does not fetch raw objects to prove their existence or digest. Chunk offsets count Unicode scalar values in normalized text, start inclusive/end exclusive. Evidence resolves response-local keys to the exact captured version and optional chunk; mismatched chunk definitions fail rather than citing existing content incorrectly. Findings retain their originating run and remain `unverified` regardless of model confidence.

Queries, sources, captures, chunks, findings/evidence and run/job/attempt success commit together. A replay of a completed attempt does nothing; a crash, rejected ownership or failed identity/generation fence leaves no partial results. Different logical runs intentionally retain separate queries/findings while reusing global source identities. Successful discoveries appear through the existing archive sources endpoint.

Document-version identity provenance remains in `metadata.content_hash`. Enrichment never relabels a raw-based identity as text-based, even when their digests coincide. `metadata.representations` separately describes the retained normalized text digest/normalization and raw storage reference/digest; `raw_sha256` remains compatible with earlier writers. Legacy undocumented raw digests remain unknown. Original version IDs, capture timestamps, raw references and producer metadata are preserved. See [captured representations](docs/database.md#captured-representations-and-hashes) for the exact metadata rules. This stabilization adds no migration and does not require a Railway schema update.

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
8. `ImplementResearchExecution` — rejects empty/Unicode-whitespace text or storage references, adds nullable finding-to-run provenance and restricts each run to one research-plan job. Existing invalid blank captures cause upgrade to fail without rewriting data. Downgrade refuses to discard populated finding/run provenance.

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
5. For a confirmed empty development destination, review all eight migrations using the idempotent script command above, then explicitly run `dotnet ef database update --project src/OrionSearch.Infrastructure` with the authorized connection supplied securely. Startup never runs migrations.
6. Re-run inspection: expect PostgreSQL 16+, all seven schemas, 15 product tables, eight known migrations and `citext`. Run the local API with the same secure connection and check `/health/ready`; `200` verifies connectivity, while schema compatibility comes from the inspection.

Use an existing Railway secure access method or tunnel if private networking prevents local access. Do not publish a database port automatically. Never set `ORIONSEARCH_TEST_POSTGRES` to Railway: that fixture creates and drops databases.

ORION-12 hosted verification ran on 2026-10-18. With explicit provisioning approval, the `development` environment (`da350d4f-39de-45ff-99d4-3bb9a9df08e3`) was created in `lucid-motivation` (`255809c6-10b6-4206-bd8e-3999e9ef1681`). Its `Postgres` service (`2a43ab11-a0c7-42bd-beb5-8b9f5db8dfa1`) runs PostgreSQL 18.6 with a persistent volume and no public domain or TCP proxy. Railway generates and retains its credentials. The pre-existing empty service named `development` in `production` was left unchanged.

The authenticated Railway console ran the read-only inspection against `orionsearch_development`, confirming no user tables, product schemas or migration history before changes. The reviewed idempotent SQL was uploaded temporarily, its SHA-256 checked against the local reviewed file, and all eight migrations applied with `ON_ERROR_STOP`. Inspection afterward confirmed seven schemas, 15 product tables, eight migration entries, `citext` 1.8 and the new capture/provenance constraints and unique job index. A temporary read-only pre-deploy setting used during access setup was cleared; no automatic migration hook remains. A clean redeploy retained the schema and cleared the temporary setup uploads. No destructive fixture, application-data seeding or production migration ran on Railway. The API was not deployed, and API-to-Railway readiness remains unverified; the local Production smoke test used an isolated local database.

Initial ORION-12 verification on 2026-10-18 established the existing 223-test baseline, then passed **322 tests** with zero failures or skips on isolated PostgreSQL 18.6. Restore, tool restore, a zero-warning build, formatting and the EF pending-model check passed. Coverage includes authorization/idempotency, a controlled commit barrier, concurrent claims/recovery, heartbeat/shutdown, final lease fencing, retries, malformed/oversized/interrupted agent responses, ingestion/replay, raw captures and migration upgrade/downgrade guards. Reviewed SQL and read-only inspection also ran against a separate empty local database. The compiled API's Production smoke returned liveness/readiness `200`, rejected spoofed research authentication with `401`, and hid OpenAPI with `404`. PostgreSQL 16 and Docker/Testcontainers startup were unavailable for this phase and remain unverified. Live LangGraph, real identity-provider integration, external raw-object integrity and API-to-Railway readiness remain separate integration gates.

The ORION-12 stabilization follow-up re-established that 322-test baseline and passed **334 tests** on isolated PostgreSQL 18.6, with zero failures or skips. Disk exhaustion interrupted an initial validation build; after space recovered, the build passed with zero warnings. To reduce peak temporary-database usage, the final suite ran as 326 non-migration tests and eight migration tests. The combined results were checked against all discovered cases, including display-name multiplicity, with none missing or added. New advisory-lock barriers cover completion beyond its original deadline, blocked heartbeat renewal, concurrent recovery and lease expiry while waiting for ownership. A held-renewal test verifies the processor joins its heartbeat before completion. Provenance regressions cover raw/text enrichment, legacy metadata, replay, exact evidence references, separate hashes and reserved metadata fields. Restore, tool restore, formatting and the EF pending-model check passed. No migration was added, and Railway access in this follow-up was read-only.

## Final backend acceptance, 2026-10-18

This review covers ORION-9 through ORION-12 on `feature/ORION-9-backend-foundation`, starting at `472e8025097d40d9b77b1c6a0fbaaa499c57dd2a`. Jira still lists all four issues as To Do. The remaining backend correction adds topic filtering to both lists; no dependency, schema, migration, Python service or deployment configuration changed. The root README now reflects the implemented .NET queue and its pending integration prerequisites.

Executed validation used .NET SDK 10.0.204 and isolated local PostgreSQL **18.6**. The baseline passed 334 tests. Before implementation, all 13 initial topic cases failed on the missing filter/validation; after implementation, 14 topic cases (including the escape character) plus the existing OpenAPI test passed. Final `dotnet test` passed **348 tests, zero failures or skips**, in 4m35s. All 348 discovered display names, including duplicate-name multiplicity, matched the final TRX results. `dotnet restore`, `dotnet tool restore`, `dotnet build` (zero warnings/errors), `dotnet format OrionSearch.sln --verify-no-changes` and `dotnet ef migrations has-pending-model-changes --project src/OrionSearch.Infrastructure` passed. The formatter first identified one initializer needing line wrapping; it was corrected before the final run. No published migration changed and no model change is pending.

The final commands ran from `services/api/`; test execution used `ORIONSEARCH_TEST_POSTGRES=Host=127.0.0.1;Port=55432;Database=postgres;Username=orion_test`. This is the task-owned local server, never Railway. Commands with their evidence options:

```powershell
dotnet restore
dotnet tool restore
dotnet build
dotnet format OrionSearch.sln --verify-no-changes
dotnet ef migrations has-pending-model-changes --project src/OrionSearch.Infrastructure
dotnet test --verbosity quiet --logger 'trx;LogFileName=acceptance-final.trx' --results-directory 'C:\Users\merte\.codex\tmp\orion-acceptance-results'
```

The critical completion/queue/commit/shutdown subset was then repeated: **22 passed, zero failures or skips**, in 1m13s. These are repeat executions within the 348 cases, not additional unique tests:

```powershell
dotnet test --no-build --filter 'FullyQualifiedName~ResearchCompletionTests|FullyQualifiedName~ResearchQueueTests|FullyQualifiedName~ResearchCommitTests|FullyQualifiedName~ShutdownLeavesAnUncommittedAttemptForLeaseRecovery' --verbosity quiet --logger 'trx;LogFileName=acceptance-concurrency-repeat.trx' --results-directory 'C:\Users\merte\.codex\tmp\orion-acceptance-results'
```

A Release artifact was also built with `dotnet publish src/OrionSearch.Api -c Release --artifacts-path C:\Users\merte\.codex\tmp\orion-acceptance-build -o C:\Users\merte\.codex\tmp\orion-acceptance-publish`. The controlled local Production smoke made 22 assertions across connected/unavailable database configurations: liveness 200; readiness 200/503 with plain Healthy/Unhealthy bodies; OpenAPI 404; all eight protected archive/research routes 401 despite caller identity headers and an unvalidated bearer token. POST/PATCH requests supplied JSON. Responses exposed no database credentials or exception details. No ResearchWorker environment override was supplied, so its disabled default applied; no production fake client exists. The initially empty local admin database still had zero product tables afterward, confirming no startup migration. Test-only exception probes separately verified generic 500 ProblemDetails in both environments. This was a local packaged-artifact smoke, not a public deployment.

Only PostgreSQL 18.6 ran in this execution. PostgreSQL 16 is not installed, and the available Docker CLI cannot reach a running daemon, so PostgreSQL 16 and container startup are **NOT VERIFIED**. Live LangGraph, real identity-provider integration and API-to-Railway readiness are also **NOT VERIFIED**.

| Issue | Requirement | Implementation evidence | Test evidence | Status |
| --- | --- | --- | --- | --- |
| ORION-9 | .NET 10/ASP.NET Core, API/Application/Domain/Infrastructure, inward dependencies | `global.json`, project references, `Program.cs`, infrastructure composition | Restore/build and startup tests | PASS |
| ORION-9 | Environment configuration, JSON logs, CORS and trusted proxy boundaries | API service registration and validated options | `ConfigurationTests`, `ReverseProxyTests`, Production smoke | PASS |
| ORION-9 | Generic ProblemDetails, health/readiness, Development-only OpenAPI, no required AI startup | API middleware/endpoints; worker defaults disabled | `ApiTests`, `ReadinessTests`, `DatabaseReadinessTests`, Production smoke | PASS |
| ORION-10 | Seven schemas, 15 product tables, eight migrations, citext and consistent history | EF configurations, unchanged migrations and snapshot | `MigrationTests`, `ArchivePersistenceTests`, EF pending-model command | PASS |
| ORION-10 | FKs, uniqueness, ownership/member constraints, evidence/chunk/version integrity, indexes, queue relationships | Persistence configurations and migration SQL | Persistence suites, catalog/index assertions and invalid-write rejection tests | PASS |
| ORION-10 | Fresh install, upgrades preserving existing data, replayable migration SQL; no startup migration | Migration chain and explicit tooling; startup contains no migration call | `MigrationTests`, `ResearchMigrationTests`; empty local DB after Production smoke | PASS |
| ORION-10 | Raw/text capture and trustworthy stored identity/representation provenance | `ResearchIngestion`, capture constraints, content fingerprint helpers | `RawCaptureTests`, `CapturePresenceTests`, normalization and provenance tests | PASS |
| ORION-10 | Railway development deployment, persistent volume and PostgreSQL version | Railway reports Online, 1/1 replica, 500 MB volume; active deployment logs identify 18.6 | Fresh read-only connector status/config/log reads | PASS |
| ORION-10 | Fresh hosted catalog, citext, tables and migration-history inspection | Existing read-only `scripts/inspect-database.sql` | No fresh SQL connection available in this execution; historical setup verification above is not a current catalog check | NOT VERIFIED |
| ORION-11 | Atomic archive CRUD/settings, soft archive and membership authorization | Archive endpoints, application service, EF store | `ArchiveApiTests`, normal-startup authentication rejection | PASS |
| ORION-11 | Explicit topic filtering, literal metacharacters, bounds, counts, ordering and archived/status combinations | Validated `topic` and parameterized SQL ILIKE predicates before count/page | `ArchiveTopicTests`, `ArchiveSourceTests` | PASS |
| ORION-11 | Stored-source titles, publication dates, language/domain, pagination and archive isolation | Source projection stays scoped to archive membership | Combined-filter and cross-user/source tests | PASS |
| ORION-11 | OpenAPI and HTTP examples | Endpoint descriptions, query DTOs and `.http` requests | `ArchiveOpenApiTests` | PASS |
| ORION-12 | Authorized POST/GET, asynchronous atomic submission and archive-scoped idempotency | Research endpoints/service/store | `ResearchApiTests`, `ResearchCommitTests` | PASS |
| ORION-12 | Live ownership after lock, completion across old deadline, joined heartbeat, generation fencing and concurrent recovery | `EfResearchQueue`, `ResearchProcessor` | `ResearchCompletionTests`, `ResearchQueueTests` | PASS |
| ORION-12 | Atomic result ingestion, bounded retry/backoff, lifecycle checks, shutdown recovery | Queue, processor, validator and ingestion | Processing/lifecycle/queue suites; rollback and recovery regressions | PASS |
| ORION-12 | Raw-only/text-only/enrichment, equal/different hashes, legacy unknowns, preserved identity, exact evidence and replay | `ResearchIngestion` and database uniqueness/FKs | Provenance, processing, output, raw-capture and evidence suites | PASS |
| ORION-12 | Typed HTTP boundary and provisional fixture compatibility | `HttpResearchAgentClient`, validator, JSON schemas/fixtures | `ResearchAgentHttpTests`, `ResearchOutputTests` | PASS |
| ORION-12 | Backend implementation available for agreed-contract integration | Explicit provisional opt-in; [AI-team handoff](../../packages/contracts/research/provisional-v1/README.md) | Isolated test authentication/fake agent cover backend flow; ORION-8 is still To Do | PASS WITH EXTERNAL DEPENDENCY |
| ORION-11 / ORION-12 | Real authentication and live API → LangGraph → Railway result flow | Provider choice/mapping, real agent and deployment still required | No real provider or compatible service available; no insecure bypass introduced | NOT VERIFIED |
| ORION-9–12 | Executed GitHub backend CI | No workflows/check runs exist on GitHub | GitHub workflows/check-run/status APIs inspected; no CI job ran | NOT VERIFIED |

The review found no additional blocker, important or minor finding requiring a backend change. No unresolved correctness, authorization, data-loss or queue-lifecycle defect was identified; this does not certify unexecuted live integration. Backend-owned implementation is ready for combined review. ORION-9/10/11 can be proposed for Done after human review; ORION-12 is code-complete with the AI/auth/deployment dependencies explicitly linked. If Done requires live integration, retain ORION-12 in review until that separate gate passes. No Jira status, description or comment was changed.

Minimal proposed ORION-10 description correction (replace only the obsolete migration tool): `Таблици users, archives, sources, archive_sources, research_jobs, findings и timestamps; EF Core миграции.` The design document maps those conceptual sources/jobs to the implemented documents/archive_documents/research_runs and system queue tables. Approval to edit the issue remains with the team.

Railway remains read-only in this acceptance pass. Project/environment/service IDs match the setup above, deployment `c89fd2c5-869d-473c-a223-ce603fc74072` is successful, the volume is mounted at `/var/lib/postgresql/data`, and no staged changes or public TCP proxy exist. SQL inspection could not be repeated: the connector has no SQL operation, no authenticated CLI is available, and the existing browser tool failed to initialize. Restore authorized console/tunnel access and run the existing inspection script before hosted acceptance; expect seven schemas, 15 product tables, citext and all eight known migration IDs. Compare any difference with the committed chain before proposing a remediation; do not migrate or reset automatically. No drift was observed through the available service metadata, and no claim is made about the uninspected catalog.

CI belongs to Velislav's ORION-16, still To Do; ORION-14/15 also remain To Do. No competing workflow was added. DevOps handoff: create a backend validation-only workflow with working directory `services/api`, .NET 10.0.204 and a PostgreSQL 16/18 service matrix. Use service database `postgres`, an isolated test role with CREATEDB, a job-local credential and `ORIONSEARCH_TEST_POSTGRES`; wait for pg_isready. Run `dotnet restore`, `dotnet tool restore`, `dotnet build --no-restore`, `dotnet test --no-build`, `dotnet format OrionSearch.sln --verify-no-changes --no-restore`, and `dotnet ef migrations has-pending-model-changes --project src/OrionSearch.Infrastructure --no-build`. Publish TRX on failure. Require no Railway secrets and include no deployment or hosted migration step. Actual workflow execution and required-check configuration remain DevOps acceptance work.

External prerequisites remain: Konstantin's agreed ORION-8 contract and real service, deterministic chunking and durable raw storage; the frontend/auth team's provider decision, validated subject mapping and user provisioning; and DevOps service deployment, credentials, trusted proxy/CORS configuration, CI and secure database inspection access. Recurring scheduling, semantic search and magazines are future product work. No public API was deployed and no worker was enabled on Railway.

See [database architecture](docs/database.md) for all 15 tables, constraint rationale, ownership, normalization assumptions and pending decisions.

## Later phases

Recurring research scheduling, the Python crawling/analysis engine, magazines and embeddings remain outside this backend implementation.

The following configuration categories are future design decisions, not settings consumed by this API:

| Category | To define when implemented |
| --- | --- |
| Authentication | Provider, trust configuration, external-subject mapping and user provisioning |
| Research service | Final ORION-8 contract and authenticated live deployment |
| Research jobs | Recurring scheduling and any public cancellation API |

CI/CD, Docker Compose, public API deployment and frontend changes are outside this issue. Authorized Railway database inspection/setup is included where the integration is available.
