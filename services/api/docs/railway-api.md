# Hosting the API on Railway

This deployment makes the existing ASP.NET Core API reachable over HTTPS in the
`development` environment of `lucid-motivation`. The process runs with ASP.NET
Core's `Production` environment, so development-only OpenAPI stays disabled.

Public availability does not configure authentication or the Python research
producer. Protected archive and research endpoints continue to return 401. The
research worker remains disabled. See [release readiness](release-readiness.md)
for the remaining product integration requirements.

## Build and runtime

Build from `services/api`:

```sh
docker build -t orionsearch-api .
docker run --rm -p 8080:8080 --env-file /secure/path/api.env orionsearch-api
```

The multistage Dockerfile restores and publishes the API and its project
references, then copies the result into the ASP.NET runtime image. It runs as
the image's unprivileged application user and listens on port 8080. Tests,
documentation, local environment files and build output stay out of the build
context. Supply secrets through Railway variables; never bake them into the
image.

Railway service settings:

| Setting | Value |
| --- | --- |
| Environment | `development` (`da350d4f-39de-45ff-99d4-3bb9a9df08e3`) |
| Source | `BolyarCoders/OrionSearch`, deployment branch pinned to an explicit commit |
| Root directory | `/services/api` |
| Dockerfile | `Dockerfile`, relative to the root directory |
| Healthcheck | `/health/ready` |
| Public domain target port | `8080` |
| Restart policy | `ON_FAILURE`, at most 3 retries |
| `PORT` | `8080` |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ASPNETCORE_HTTP_PORTS` | `8080` |
| `DATABASE_URL` | `${{Postgres.DATABASE_URL}}` (Railway reference, not a copied credential) |
| `ResearchWorker__Enabled` | `false` |

The API connects to the existing development Postgres service through Railway's
private network. Do not create a public database proxy for API access. The API
needs no persistent volume because PostgreSQL owns its durable state.

This is a manual deployment. The source is pinned so pushing another commit
does not automatically release it. This change adds no CI/CD workflow and does
not alter the team's existing backend validation workflow. Updating the service
requires selecting and verifying another explicit commit.

## Verify an actual deployment

Replace `API_URL` with the service's HTTPS domain:

```sh
curl --fail "$API_URL/health"
curl --fail "$API_URL/health/ready"
curl -i "$API_URL/archives"
curl -i "$API_URL/openapi/v1.json"
```

Expected results:

- `GET /health`: 200 with `{"status":"healthy"}`.
- `GET /health/ready`: 200 with `Healthy` when PostgreSQL is reachable.
- `GET /archives`: 401 with ProblemDetails until the real provider is configured.
- `GET /openapi/v1.json`: 404 in this runtime environment.

There is no root-page handler; `/` returning 404 is expected. These smoke checks
establish HTTPS routing, application startup, database connectivity and the
existing access boundary. Readiness checks connectivity, not schema history or
real authenticated research execution. Do not label those separate acceptance
gates complete based on a healthy deployment.

Application startup does not apply migrations. Existing published migrations
are unchanged. Any future schema operation must use the separately reviewed EF
tooling and authorized database connection. Keep the disposable test fixture
pointed at local test infrastructure, never this hosted database.

## Deployment boundaries

1. **Source of truth:** PostgreSQL owns archives, memberships, runs and queue
   state; Railway owns service configuration.
2. **Writers:** Existing API/application services retain all product writes.
   Deployment adds no seed users, migration hook or alternate data writer.
3. **Dependencies:** Existing API → application/domain/infrastructure boundaries
   remain unchanged; the container only packages the API entry point.
4. **Service contract:** Public HTTPS routes expose the existing API. The database
   connection stays private; credentials never enter public responses.
5. **Failures:** Railway checks database readiness before routing a replacement
   deployment. Process liveness remains independent of database availability.
6. **Lifecycle:** Railway owns process restarts; existing database transactions
   and queue leases retain their recovery semantics. The worker is disabled.
7. **Verification seams:** Existing API/PostgreSQL tests run locally; public HTTP
   smoke checks exercise the actual packaged deployment separately.
8. **Compatibility:** No schema or endpoint contract changes. Roll back to a
   previously verified image/commit without a database downgrade.
9. **Observability:** Railway deployment logs and `/health` versus `/health/ready`
   distinguish process availability from database connectivity.
10. **Security:** Run as an unprivileged user, keep Production OpenAPI disabled,
    preserve fail-closed authentication and configured CORS/proxy trust, and
    supply database credentials through a Railway reference.
