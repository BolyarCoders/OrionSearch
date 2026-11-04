# Render API and PostgreSQL hosting

## Partial live release: 2026-10-29

The API at https://orionsearch-api.onrender.com is live on commit `20bb396`;
Render deploy `dep-dbhkgcnlk1mc73d067qg` was triggered manually from the
review branch. The service's configured branch remains `main` and automatic
deployment remains off. PR #12 contains the changes for review and has not
been merged. Do not redeploy the older `main` before it contains this release.

The PostgreSQL 18 database is at eleven migrations, with external IP access
closed. Before upgrading, an encrypted operator-local export was made and an
earlier export was restored into isolated PostgreSQL 18 and upgraded from eight
to eleven migrations. After release, HTTPS readiness, registration, login,
authenticated profile and archive create/read, plus unauthenticated rejection,
were exercised. Only the synthetic account and archive were removed afterward.

Native Identity and B2 capture reads are enabled. The API's encrypted PFX and
password are held in Render environment secrets; a recovery copy encrypted to
the operator's Windows user account is under `OrionSearch/protected-backups` in
that user's LocalAppData. This copy is not independently recoverable if that
Windows profile is lost: arrange a separate protected recovery copy and periodic
backups. The B2 producer/worker is not deployed as part of this release, so
research jobs can remain queued; successful archive/account flows do not imply
working research execution. B2's hosted upload path has not been verified.

Free Render Postgres expires on 2026-11-26 at 06:35 UTC and has no managed
backups. No recurring backup job or replacement database has been configured.
This is a limited live deployment, not full production acceptance. The remaining
gates in [mvp-acceptance.md](mvp-acceptance.md) still apply.

The Blueprint sets `ReverseProxy__TrustRenderEdge=true` for this Free public
web service. Render documents that public traffic reaches the service through
Cloudflare and that its edge overwrites `CF-Connecting-IP`; Free web services
cannot receive private-network traffic. Both per-IP rate limiters use that one
address, not the caller-supplied `X-Forwarded-For` chain. Missing or malformed
values share a conservative `unknown` bucket. This trust relies on that specific
ingress isolation: do not enable it on a private service, a host reachable
directly, or a paid service accepting private-network requests. Reassess the
boundary before changing plans, adding a proxy or enabling private ingress.
Elsewhere the application uses the socket address or explicitly trusted forwarded
proxies. Render edge mode fails startup outside a Render web service.

Blueprint sync is paused: this checked-in setting does not change the existing
service until an operator applies the reviewed configuration and deploys it.
Local tests simulate the edge contract; a hosted multi-client and spoofing check
is still required after deployment. References: [Render edge guidance](https://render.com/articles/host-pocketbase-on-render),
[Render private networking](https://render.com/docs/private-network).

The root [render.yaml](../../../render.yaml) declares one API and one PostgreSQL
database for `BolyarCoders/OrionSearch`. The deployment destination is Render's
`My Workspace`, workspace ID `tea-db7tmovavr4c73dv8nog`, explicitly confirmed by
the repository owner on 2026-10-27. The workspace name differs from the GitHub
organization; check both identities before provisioning or changing resources.

## Verified deployment: 2026-10-27

**Live API:** https://orionsearch-api.onrender.com

| Item | Verified value |
| --- | --- |
| Blueprint | `exs-dbg4bekipgvc73efnnd0` |
| API service | `srv-dbg4cluc6qcc73ea9qs0` |
| Live deploy | `dep-dbg4cmec6qcc73ea9seg` |
| Deployed commit | `a6716f7b14ed19a9b03425a5d56c627e32505267` |
| Database | `dpg-dbg4c5uc6qcc73ea8590-a` |
| Both compute plans / region | Free / Frankfurt |
| Database expiry | **2026-11-26 at 06:35 UTC** |
| Service auto-deploy / Blueprint auto-sync | Off / No (Sync paused) |
| External database IP rules after setup | Empty; private access only |

Render completed the actual Docker build and marked the deployment `live`.
Public HTTPS checks passed both before and after schema initialization:
`/health` 200, `/health/ready` 200, `/archives` 401 and `/openapi/v1.json` 404.
The current Blueprint passed Render JSON Schema validation and
`render blueprints validate render.yaml --output json` (`valid: true`).

The operator verified the destination was empty, then explicitly applied all
eight EF migrations. Hosted inspection confirmed PostgreSQL 18.6, seven logical
schemas, fifteen product tables, `citext` 1.8, all migration-history entries and
constraints including the composite chunk/version evidence foreign key. A
second EF update applied nothing and reported the database already up to date.
External setup access allowed only the operator's IP temporarily and was removed
after inspection. External connections used hostname/certificate verification;
libpq's missing default CA file was resolved using the machine's trusted root
certificates without disabling TLS verification.

This is a fresh schema with no seeded users or application records. Railway
records were not copied and Railway resources were not changed. The previous
348-test local run belongs to 2026-10-21; it was not rerun for this hosting change.
A local container rebuild encountered a registry TLS handshake timeout; the
successful Render Docker build and deployed HTTP checks are the packaging
evidence for this release. No new migration or CI/CD workflow was introduced.

## Resources and boundaries

| Resource | Configuration |
| --- | --- |
| API | `orionsearch-api`, Docker web service, Free compute plan |
| Database | `orionsearch-postgres`, PostgreSQL 18, Free compute plan |
| Logical database / user | `orionsearch` / `orionsearch` |
| Region | Frankfurt for both resources, allowing private database connections |
| Release source | `main`; automatic service deploys disabled |
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
   current Render schema. Review the configuration on a feature branch and
   merge it into `main` before selecting `main` as the Blueprint source.
2. In the confirmed workspace, create a Blueprint from the public repository
   URL and explicitly select `main` with path `render.yaml`.
   Review the two Free plans and private database rule before applying.
3. Confirm both resources are in Frankfurt and the API's `DATABASE_URL` comes
   from the new database's internal connection string. Keep automatic deploys
   and automatic Blueprint sync disabled.
4. Initialize the new database through an explicitly authorized operator
   connection as described below. Verify migration history and constraints.
5. Wait for the API's specific deployment to become live. Verify the public
   HTTPS behavior below before reporting availability. Record the URL, resource
   IDs, deploy ID, commit, database expiry and actual results.

The initial deployment above used `codex/render-deployment` before PR review.
The reviewed configuration targets `main` for subsequent releases. After the PR
is merged, retarget the existing Blueprint to `main` and manually sync it; both
automatic Blueprint sync and automatic service deploys remain disabled. Changing
the file alone does not retarget or redeploy the existing live service.

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
