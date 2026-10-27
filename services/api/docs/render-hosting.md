# Render API and PostgreSQL hosting

The root [render.yaml](../../../render.yaml) declares one API and one PostgreSQL
database for `BolyarCoders/OrionSearch`. The deployment destination is Render's
`My Workspace`, workspace ID `tea-db7tmovavr4c73dv8nog`, explicitly confirmed by
the repository owner on 2026-10-27. The workspace name differs from the GitHub
organization; check both identities before provisioning or changing resources.

## Resources and boundaries

| Resource | Configuration |
| --- | --- |
| API | `orionsearch-api`, Docker web service, Free compute plan |
| Database | `orionsearch-postgres`, PostgreSQL 18, Free compute plan |
| Logical database / user | `orionsearch` / `orionsearch` |
| Region | Frankfurt for both resources, allowing private database connections |
| Source | `codex/render-deployment`; automatic service deploys disabled |
| Docker context / file | Repository root / `services/api/Dockerfile` |
| Runtime | ASP.NET Core Production, HTTP port 10000 |
| Healthcheck | `/health/ready` |
| Database credentials | Render `fromDatabase.connectionString` reference |
| Database public access | Denied by default with `ipAllowList: []` |
| Research worker | Explicitly disabled |

Render creates the database password. No password, external connection URL or
API key belongs in this file, the Blueprint, logs or PR text. Application data
remains in PostgreSQL; the API requires no persistent disk. The application
still authenticates through its fail-closed unconfigured scheme. Public hosting
does not provide login, registration or accepted real-agent integration.

This deployment preserves Railway resources. Creating and initializing the
Render database does not copy records from Railway. Any record migration
requires a verified source export, a reviewed restore and row-count/integrity
checks; do not delete or overwrite the source as part of hosting setup.

The Docker SDK image is pinned to the published `10.0.402` image. The local SDK
selector remains unchanged. The existing root-context Docker/Compose layout is
preserved; no CI/CD workflow is added or modified.

## Free-plan limits

Verified against Render's current documentation on 2026-10-27:

- Free API compute: 512 MB RAM, 0.1 CPU, one instance. The workspace receives
  750 free instance hours per calendar month, shared across free web services.
- An idle free web service sleeps after 15 minutes and wakes on a later request.
  Cold starts can take about a minute. Background processing is not reliable
  while a free web process is sleeping; the worker remains disabled.
- Free PostgreSQL: 256 MB RAM, 0.1 CPU, at most 100 connections and 1 GB storage.
  Only one free PostgreSQL instance can be active in a workspace.
- **The free database expires 30 days after creation.** It becomes inaccessible
  unless upgraded. Render deletes it after the following 14-day grace period.
  Record the actual dashboard expiry date when the database is created.
- Free PostgreSQL has no managed backups. Plan an authorized export before
  expiry; do not assume this is permanent free database hosting.
- Free web services have no shell, one-off jobs or paid pre-deploy commands.
  Schema initialization therefore uses explicit EF tooling from an authorized
  operator connection rather than an automatic startup migration hook.

References: [free services](https://render.com/docs/free),
[compute plans](https://render.com/docs/compute-plans),
[private networking](https://render.com/docs/private-network),
[Blueprint specification](https://render.com/docs/blueprint-spec).

## Provision and release

1. Validate `render.yaml` using `render blueprints validate render.yaml` and the
   current Render schema. Push the configuration to its feature branch.
2. In the confirmed workspace, create a Blueprint from the public repository
   URL and explicitly select `codex/render-deployment` with path `render.yaml`.
   Review the two Free plans and private database rule before applying.
3. Confirm both resources are in Frankfurt and the API's `DATABASE_URL` comes
   from the new database's internal connection string. Keep automatic deploys
   and automatic Blueprint sync disabled.
4. Initialize the new database through an explicitly authorized operator
   connection as described below. Verify migration history and constraints.
5. Wait for the API's specific deployment to become live. Verify the public
   HTTPS behavior below before reporting availability. Record the URL, resource
   IDs, deploy ID, commit, database expiry and actual results.

## Explicit database initialization

Application startup never applies migrations. All eight existing migrations
remain the source of truth; no new migration is required to change hosting.

The database normally denies external connections. If local operator tooling
is needed, temporarily allow only the operator's public IP after explicit
authorization, use the external connection URL with TLS, then remove that rule.
Do not allow `0.0.0.0/0`, copy the external URL into the API, or run the disposable
test fixture against Render.

Supply the authorized external connection through the operator process's
`DATABASE_URL` environment variable without echoing or committing its value.
From `services/api`, use:

```sh
dotnet tool restore
dotnet ef database update --project src/OrionSearch.Infrastructure
psql --file scripts/inspect-database.sql
```

`psql` needs its own secure connection environment (`PGHOST`, `PGDATABASE`,
`PGUSER`, `PGPASSWORD`, `PGSSLMODE`) or equivalent operator configuration. It
does not consume the application's `DATABASE_URL` variable automatically.
External clients should use the full Render hostname and TLS; verify the
provider's certificate rather than disabling verification.

Before applying migrations, confirm the hostname/resource ID identifies the
new Render database and inspect its existing objects. Stop on an unexpected
nonempty schema. Afterward confirm eight ordered migrations in
`system.__ef_migrations_history`, `citext`, seven logical schemas, fifteen product
tables and the expected constraints. Reapplying EF update should report no
pending migrations. Remove temporary external access after verification.

## Public smoke checks

For the actual deployed HTTPS origin:

| Request | Expected result |
| --- | --- |
| `GET /health` | 200, `{"status":"healthy"}` |
| `GET /health/ready` | 200, `Healthy` |
| `GET /archives` | 401, ProblemDetails |
| `GET /openapi/v1.json` | 404 in Production |

There is no root-page handler. A 404 at `/` does not mean the API is down.
Readiness verifies database connectivity, not migration compatibility. Keep the
schema inspection and public checks as separate evidence. Authentication,
real producer integration and product release acceptance remain in the
[release-blocker register](release-readiness.md).

Rollback means deploying a previously verified compatible application commit.
It does not mean dropping tables, downgrading the database or deleting Railway
resources. Preserve the database and investigate a failed release before retrying.
