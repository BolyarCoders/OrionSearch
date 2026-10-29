# Dedicated execution and result reads (P4)

Implementation plan, 29 October 2026, feature/backend-integration after 4b5f620.
Scope: a separate .NET host over the existing executor, archive-scoped result
reads and a frontend polling contract. No hosted activation, provider spending,
frontend UI, synthesis implementation or CI/CD changes are included.

| Architecture question | Decision |
| --- | --- |
| Source of truth | PostgreSQL owns runs/jobs/attempts/findings and archive/version grants. Result DTOs and counts are derived; no report cache or second queue is introduced. |
| Ownership | Existing .NET ResearchProcessor/queue/ingestion remain the only execution writer. API reads do not transition runs or call the agent. |
| Dependencies | Worker references Infrastructure and its Application contracts, not the API project. API maps Application read DTOs. Domain has no hosting dependency. |
| Service boundary | Worker uses the same versioned agent HTTP and S3 contracts. Public reads expose run/finding/document/version IDs and safe excerpts, never queue IDs, credentials, payloads or storage locators. |
| Failures | Dedicated startup rejects disabled/unconfigured execution and unavailable/outdated DB schema. Runtime retries/cancellation/fencing remain unchanged. Missing or inaccessible resources return 404. |
| Lifecycle | One job at a time per host, explicit enablement, generic host cancellation on shutdown, unsettled attempts recovered by the existing lease mechanism. No migration on startup. |
| Test seams | Actual generic host with injected test agent; PostgreSQL-backed HTTP tests, real Identity login flow and controlled barriers for shutdown/recovery. No production fake fallback. |
| Compatibility | Additive GET routes and a new executable. API-embedded execution remains opt-in using the same flag. No schema migration: existing run/archive, finding/run, evidence/version and grant indexes serve these queries. Roll back by stopping the dedicated host and removing additive routes; no data rewrite. |
| Observability | Existing finite failure categories and run/attempt/commit logs. Public history exposes safe persisted status/counts and retry time. Process liveness does not imply live provider acceptance. |
| Security | Active user plus persisted archive membership before reads. Findings require completed run provenance in that archive; evidence additionally requires that archive's exact version grant. Legacy findings without run provenance are hidden. Limits bound pages and legacy text fields. |

Implementation order: read contracts/service/store and SQL-scoped projections;
HTTP mapping and authorization tests; worker host/preflight and lifecycle tests;
container packaging, local integration and operations/polling documentation.

## Query invariants

History is scoped to archive_id before filtering/counting/paging, with createdAt
descending/id ascending ordering. It includes legacy runs without queue jobs,
using zero attempts and the existing safe statistics/error projection.
Findings are scoped through research_runs.archive_id and completed status before
counting/paging; firstObservedAt descending/id ascending breaks ties. Status/type
and optional run filter narrow that scope. A run filter outside the archive
returns 404. Evidence is joined through the already authorized finding and exact
archive/version grant, never inferred from a shared document URL. Counts use the
same permission predicate as returned evidence. Source detail requires the
archive_document link; version counts use explicit grants, not global versions.

The existing indexes cover archive history (archive_id, created_at), findings by
run, evidence by finding and grants by (archive_id, document_version_id). No new
canonical datum, index or migration is necessary for these bounded reads.

## Public read contract

All routes require configured Native Identity access tokens, an active user and owner/editor/viewer archive
membership. A nonmember gets 404. Pause/archive preserves existing reads, as in
source listing. Findings without researchRunId, findings from non-completed runs
and findings belonging to other archives are not returned.

| Route | Query / response |
| --- | --- |
| GET /archives/{archiveId}/research-jobs | page/pageSize, optional status; Page<ResearchStatus>, same safe fields as GET /research-jobs/{id}. |
| GET /archives/{archiveId}/findings | page/pageSize, optional status/type/runId; Page<FindingSummary>. runId must belong to this archive or returns 404. |
| GET /archives/{archiveId}/findings/{findingId} | FindingDetails: finding summary plus a page of EvidenceReference. page/pageSize paginate evidence only. |
| GET /archives/{archiveId}/sources/{documentId} | SourceDetails: SourceSummary plus versionCount from this archive's explicit version grants. |

Pages default to 1/20; page <= 1000000 and pageSize <= 100, both positive.
Invalid filters return 400. Research statuses: queued/running/completed/failed/
cancelled. Finding statuses: unverified/supported/disputed/superseded; types:
claim/observation/estimate. Confidence is a model value, not a verification badge.
Finding statements and quotes are bounded to 10000 PostgreSQL characters, with
statementTruncated/quoteTruncated marking larger legacy text; evidence source
titles are capped at 2000. Detail evidence is ordered by evidence id ascending.
Pages describe live data, not an immutable multi-request snapshot; refresh a
list after new results commit. GET responses use no-store.

Example finding detail (IDs abbreviated here, use UUIDs on the wire):

```json
{
  "finding": {
    "id": "finding-uuid", "researchRunId": "run-uuid",
    "statement": "A statement grounded in a source", "statementTruncated": false,
    "type": "observation", "status": "unverified", "confidence": 0.7,
    "firstObservedAt": "2026-10-29T00:00:00Z", "evidenceCount": 1
  },
  "evidence": {
    "items": [{
      "id": "evidence-uuid", "documentId": "document-uuid", "versionId": "version-uuid",
      "chunkId": "chunk-uuid", "chunkIndex": 0, "stance": "supports",
      "quote": "preserves evidence", "quoteTruncated": false,
      "canonicalUrl": "https://example.com/source", "sourceTitle": "Research source"
    }],
    "pageNumber": 1, "pageSize": 20, "totalCount": 1
  }
}
```

Follow documentId/versionId into the P3 versions/text/download routes. Quotes are
plain text; the client must escape them rather than render arbitrary HTML.
evidenceCount/totalCount count only evidence with the exact granted version;
they do not reveal hidden global versions. A linked discovery has versionCount=0
and remains readable as source metadata. No narrative report, global search or
automatic finding verification is invented by these endpoints.

## Polling from a client

1. Register/login through Native Identity; preserve the existing short-lived
   access/refresh and logout behavior. No email confirmation or SMTP is needed.
2. Create an archive, then POST /archives/{id}/research-jobs. Use a stable
   Idempotency-Key for retries of that same submission. Save the returned run id
   and Location; do not infer an internal queue job id.
3. Poll GET /research-jobs/{id} about every 2 seconds while queued/running.
   The response supplies Retry-After for these nonterminal states. Respect 429
   or transient network errors with bounded backoff (up to 30 seconds); a client
   may stop polling on navigation without cancelling the durable job.
4. retrying=true means the same run is queued for another attempt; nextAttemptAt
   is a hint, not permission to submit a duplicate. A paused archive can keep a
   run queued. Do not display this as a new research result.
5. Stop on completed/failed/cancelled. On completed refresh the archive's sources
   and findings. Zero discoveries, zero captures or zero findings are valid
   counts; do not wait indefinitely for a synthetic report. On failure show the
   safe error summary. Stop on 401 (refresh/login flow) or 404 (no access/missing).

apps/web currently contains only a placeholder. These contracts enable a client;
this package does not claim that a frontend polling screen is implemented.

## Dedicated worker configuration

Run from services/api/src/OrionSearch.Worker after applying reviewed migrations
manually to the intended database. No migration is performed by worker startup.
The dedicated host checks connectivity and an exact match of applied/compiled
migration history within 10 seconds; missing or unknown future migrations fail
startup with a safe message. A regression test proves an empty DB remains empty.

```text
ConnectionStrings__OrionSearch=<same-database-as-api>
ResearchWorker__Enabled=true
ResearchWorker__AgentBaseAddress=https://<trusted-research-origin>/
ResearchWorker__AgentApiKey=<service-secret>
```

Use `dotnet run` there, or from the repository root:

```sh
dotnet run --project services/api/src/OrionSearch.Worker
docker build -f services/api/Worker.Dockerfile -t orionsearch-executor .
docker run --rm --env-file <private-worker-env-file> orionsearch-executor
```

Use the P3 CaptureStore settings/readonly credentials on the executor when Python
captures are enabled. Python receives only its upload credentials and the shared
service key, never PostgreSQL credentials or user tokens. Secrets must stay in
the secret manager or ignored local configuration, not in tracked files.

API-embedded execution stays disabled unless deliberately enabled. In dedicated
mode set ResearchWorker__Enabled=false on the API and true on the executor.
The same processing implementation and PostgreSQL fencing support multiple
executors, but this package defaults to one sequential job per process. Python
currently has one execution slot; do not add replicas to bypass provider budgets.
Defaults remain: lease 120s, heartbeat 30s, poll 5s, call timeout 300s, 3 attempts,
retry base/max 10/300s and agent response 2 MiB. API submission snapshots attempt
limits into each job; changing worker settings does not rewrite queued payloads.

Only HTTPS or the existing explicit loopback/Railway private HTTP exception is
accepted for the agent. Plain HTTP at an arbitrary Docker DNS name is not added
to the trust policy. The .NET host is a background process, with no public HTTP
listener, /health endpoint, auth bypass or service-to-user token conversion.

SIGTERM/Ctrl+C cancels current work; host shutdown has a 30s budget. A cancelled
in-flight attempt is left processing until lease expiry/recovery; no fabricated
failure/success or partial ingestion is committed. New hosts use the same
RecoverAsync, SKIP LOCKED, heartbeat, retry and completion fencing. Startup
requires explicit Enabled=true and a valid agent address/key; a disabled or
incomplete dedicated host exits instead of presenting an idle successful service.

Use structured run/attempt/committed/failure logs and public queued/running state
to diagnose work. Process liveness alone is not a provider readiness check. The
worker has no new heartbeat table, scheduler or second job lifecycle. Its DI
graph omits user-facing services entirely; it never creates a fake current user.

No Render service is created/enabled here. Hosting capacity/cost, trusted network,
private storage policies, real provider synthesis, backup/restore and externally
reviewed acceptance remain P5/product gates. The free plan is not silently
upgraded to obtain always-on background execution.

## Verification record

The local product-flow test uses real Native Identity,
the dedicated generic host, PostgreSQL and versioned S3; only the research
producer is a fixture. It is not live Gemini or hosted acceptance.

| Gate | Result / evidence (2026-10-29) |
| --- | --- |
| Baseline | PASS: existing ResearchApiTests/ArchiveOpenApiTests, 17 passed before implementation. |
| Dedicated DI regression | Reproduced missing ICurrentUser under strict service-provider validation; fixed by omitting user-facing registrations in worker mode. The regression passes without introducing a fake current user. |
| Full .NET backend suite | PASS: dotnet test services/api/OrionSearch.sln --configuration Release, 438 passed, zero failed/skipped; p4-backend.trx. PostgreSQL 16 and actual versioned local S3 are used. |
| PostgreSQL 18 targeted suite | PASS: ResearchResultsTests/WorkerHostTests/NativeResearchFlowTests, 22 passed, zero failed/skipped; p4-postgres18.trx. |
| Native account flow | PASS within the suites: register/login → create archive → 202 queued before execution → dedicated host → completed → finding/evidence/source/text/raw reads; another real account receives 404 on all resource routes. Upstream research producer is a fixture. |
| Restart/shutdown and startup | PASS: a cancelled host leaves no partial results, a new host recovers the expired attempt and commits once; disabled/missing/unsafe agent settings fail; empty/unknown-future migration histories are not modified and do not start processing. |
| Worker packaging | PASS: docker build -f services/api/Worker.Dockerfile -t orionsearch-executor:p4 .; actual Linux image starts against an explicitly migrated, task-owned database, logs application start, runs as UID 1654, then exits 0 on SIGTERM. With execution disabled/unconfigured, startup is refused. No jobs were submitted in this image smoke, so no provider was called. |
| API packaging | PASS: docker build -f services/api/Dockerfile -t orionsearch-api:p4 .; actual image boots against that database; /health and /health/ready return 200, protected findings returns 401 with default auth disabled. |
| Build/model/format | PASS: Release worker/API publish; EF has-pending-model-changes reports no changes; dotnet format --verify-no-changes --no-restore; git diff --check; updated local Markdown links resolve. |
| Real provider/hosted/frontend | UNVERIFIED: Gemini/real article synthesis, private hosted S3 policy/quotas, Render activation, actual frontend polling screen, backup/restore and reviewed product acceptance. No hosted schema, worker, authentication flag or paid service was changed. |

Image IDs:

- Executor: sha256:29f5237965afead978e3002b48a48d484a8ec339b625e0b20ec247c14708f957.
- API: sha256:452ff528dc7c65493528eb77c55778b2ad3bfe9b35b1e53bd4c8eb85d3727561.

TRX/build logs live outside the repository under the task's temporary directory.
The manually started smoke containers/network were removed. Python source was
not changed in P4; the 96-test P3 Python result is historical, not a fresh P4
verification claim. No release sign-off is inferred from these local checks.
