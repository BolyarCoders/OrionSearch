# P5 acceptance and operational hardening

Candidate branch feature/backend-integration, starting at cbb2708, 2026-10-29.
This is a local acceptance package, not production sign-off. No hosted writes,
paid resources, automatic deployment, main push or CI/CD changes are authorized
by this package. Existing production data and private network rules stay intact.

## Architecture decisions before implementation

| Question | Decision |
| --- | --- |
| Source of truth | Existing PostgreSQL runs/jobs/attempts remain canonical. Admission and dispatch counters are derived from them, never process-memory balances. |
| Ownership | The API admits work under an atomic global admission lock. Existing queue claims reserve capacity/attempt budget under a separate lock. Python never decides public quotas. |
| Dependencies | Application defines limit options and admission contract; Infrastructure counts existing persisted rows using database time. API maps quota refusal to safe 429/Retry-After. |
| Boundary | Public request/result shape stays intact. No client quota override, provider key, bucket credential or paid-token balance crosses the boundary. |
| Failures | Refused submissions create no run/job; matching idempotency replay returns its existing run before quota checks. Saturated dispatch leaves jobs queued, not failed. Dependency failure cannot bypass a limit. |
| Lifecycle | Daily windows are UTC database dates; per-minute admission windows use DB time. Pending/processing jobs consume outstanding capacity; every reserved attempt, including interrupted/retried work, consumes dispatch budget. Restart cannot reset usage. |
| Test seams | Real isolated PostgreSQL, concurrent requests/workers, controlled persisted timestamps and existing agent/storage seams. Restore drill uses disposable source/target databases and local versioned storage only. |
| Compatibility | No new canonical counter/table is needed. Budget is charged to the archive owner (including editor submissions), explicitly shared by that owner's archives. Existing rows count, so legacy jobs cannot evade usage limits. Existing migrations/data are not rewritten. |
| Observability | Safe 429 with retry hint for admission; claim refusal preserves queued state. Acceptance report records exact results, commits, image IDs and unresolved hosted gates. |
| Security | Auth/member checks precede durable quota information; IP request limiting applies before authentication and complements atomic owner/global budgets. New account/archive/idempotency keys cannot bypass global capacity or attempt ceilings. A token/currency spending meter is not claimed. |

## Work plan

1. Reproduce missing admission/dispatch bounds with regression tests, then add
   finite configurable owner/archive/global limits using existing data.
2. Exercise backup/export and restore into a separate local target, preserving
   account/token keys, jobs, evidence and immutable object references; verify
   hash/authorization after restore. No production export or source deletion.
3. Run security/failure suites, package checks and read-only hosted checks;
   publish PASS/FAIL/UNVERIFIED evidence and a concrete operations runbook.

The owner budget deliberately charges collaborators to the owner of the archive.
It is not a per-editor fairness/accounting feature. Default limits are conservative
operator-configurable ceilings for a small demonstration, not a provider's price
or quota promise. LLM token/currency budgets and real-provider synthesis remain
separate release gates until actual provider usage can be accounted for.

Only two non-destructive indexes are added: research_runs.created_at supports
global/owner UTC-day and minute admission counts; job_attempts.started_at supports
UTC-day dispatch counts. Existing archive/run/job foreign-key indexes support
the owner joins. The forward migration adds no table/counter and rewrites no
data; rollback drops only those derived indexes. Fresh/upgrade verification uses
the permanent migration suite. Do not apply this migration to the hosted database
as part of local acceptance.

## Admission and dispatch configuration

All options are under ResearchLimits (environment spelling: ResearchLimits__Name).
Use the same reviewed limits on every API/executor replica. Limits are always
finite and positive; zero is not a disable/bypass switch.

| Setting | Default | Counts |
| --- | --- | --- |
| MaxOutstandingGlobal | 100 | All research_plan jobs pending/processing |
| MaxOutstandingPerOwner | 10 | Outstanding jobs across the owner's archives, including editor submissions |
| MaxOutstandingPerArchive | 5 | Outstanding jobs for that archive |
| MaxRunsPerOwnerPerDay | 20 | Persisted runs created on the current UTC database date |
| MaxRunsGlobalPerDay | 100 | Persisted runs across all owners on that UTC date |
| MaxRunsPerOwnerPerMinute | 5 | Rolling 60-second persisted run count across the owner's archives |
| MaxSubmissionsPerIpPerMinute | 20 | HTTP fixed window per API process, before model binding; no limiter queue |
| MaxConcurrentAttempts | 1 | Live, non-expired processing leases across cooperating executors |
| MaxAttemptsPerOwnerPerDay | 30 | Every reserved research attempt for the owner, including failures/retries |
| MaxAttemptsGlobalPerDay | 100 | Every reserved research attempt globally on that UTC date |

Owner/archive/idempotency checks occur before durable quota checks. Matching
idempotency replay does not consume another run budget; incompatible reuse stays
409. The IP limiter applies to all submissions/replays and may still return 429.
Denied work returns generic ProblemDetails, no-store and Retry-After (60 seconds
for minute/capacity limits, until next UTC day for daily limits). This is a retry
hint, not a guaranteed reservation. The POST research body is capped at 16 KiB
before binding; Kestrel also enforces this for chunked bodies. Oversize is 413.
Authentication/account routes retain their separate 32 KiB body/account limits.

Both admission and claims use distinct transaction-scoped PostgreSQL advisory
locks (713904/1 and 713904/2) only around local DB work. Admission inserts the run
and job in the same transaction; CreatedAt comes from DB time after admission,
not the start of a transaction that might have waited across midnight. Claim
reserves the attempt before calling the provider; it rechecks the archive and
assigns the lease after row-lock waits. Crossing UTC midnight during that wait
returns without mutation so the next poll uses fresh budget counts. Existing
lease/heartbeat/fencing rules still gate dispatch/completion.

When dispatch is capped, jobs remain queued and the worker polls normally. A
saturated owner's candidates are skipped so another owner can proceed. No
artificial failed result is created. Existing historical rows count; restarting
or choosing a new archive/key cannot reset durable usage. Paused/archived pending
work counts until the existing recovery policy cancels/settles it. No public API
deletes run/attempt history; an operator retention policy must preserve usage if
it later purges current-window data.

MaxConcurrentAttempts bounds valid leases, not the number of upstream calls that
could overlap after a network partition/expired worker. At-least-once execution
and finite attempts remain explicit. Provider tokens/currency are not measured
by these counters; do not interpret them as a guaranteed monetary cap.

## Restore runbook: operator-controlled, isolated first

The permanent RestoreAcceptanceTests uses actual pg_dump custom format and
pg_restore into a separate empty database, encrypted Identity keys, and a local
S3 byte export/re-upload with an explicit immutable-reference map. The source
database/objects are not deleted or overwritten. This is local proof of the
procedure, not evidence of a configured hosted backup service, RPO or RTO.

1. Identify the authorized source and a **separate empty target**. Record source
   commit/schema history, PostgreSQL version, migration tool version, intended
   RPO/RTO and retention policy. Use a matching/newer compatible pg_dump/restore
   version, TLS and an operator role; never put passwords on a command line or
   commit connection strings. Do not use test fixtures against hosted databases.
2. Stop/drain all API writes and executors; stop agent uploads or wait for their
   bounded work to settle. Keep the old environment fenced through cutover. A DB
   snapshot alone is not a consistent snapshot of external objects or revocations.
3. Take an encrypted, access-controlled custom-format pg_dump, including all
   schemas, extension DDL, constraints, migration history, auth users/stamps,
   encrypted system.data_protection_keys, jobs/attempts and archive/version grants.
   Capture object-version inventory from **both** document_versions.storage_uri
   and archive_document_versions.storage_uri; copy verified bytes, media type,
   digest and exact versioned references. Retain the external PFX/private key and
   its password in a separate protected recovery system. They are not in the DB.
4. Restore into the isolated empty target with pg_restore --exit-on-error, using
   reviewed ownership/ACL mapping. Do not use --clean against the original. Check
   every tool exit code. Keep application consumers stopped until DDL completes;
   recreate pools/reload Npgsql types if a pre-restore connection cached the
   catalog (notably citext OIDs). Never restore over active API connections.
5. Verify schema/history/constraints and row counts. Do not run startup migration
   hooks to hide a mismatched restore. If external objects preserve their exact
   immutable version IDs, retain those references. If re-upload creates new IDs,
   verify every byte/hash and build an exact old-to-new immutable mapping. Apply
   the reviewed mapping in one transaction to both global version and archive
   grant references, plus representations.raw.storage_uri in metadata. Retain
   digest/content identities; do not substitute mutable URLs, feed summaries or
   invented extracted text. No generic destructive remapping tool is shipped.
6. Before enabling the target, invalidate all restored sessions by rotating user
   security/concurrency stamps. Keep the encrypted Data Protection keys/certificate
   for their proper recovery lifecycle; require fresh login. Fence restored
   processing leases (clear worker identity/expire lease) and let the existing
   recovery path settle interrupted attempts before dispatch. A stale source
   executor must never be allowed to write to the target. Reconcile disabled
   users, password changes and administrative revocations that happened **after**
   the snapshot from a separately protected audit record. Stamp rotation alone
   cannot reconstruct those lost changes; security review is required.
7. In the isolated target, verify real login/new token issuance, old access and
   refresh rejection, archive membership isolation, exact quote/chunk references,
   raw downloads/hash verification, queue recovery and stale-attempt rejection.
   Confirm source data/objects are still retained. Measure actual elapsed restore
   time/data loss against the chosen RTO/RPO; those targets are not set by a test.
8. Cut over only with the named operational/security reviewer and a recovery
   snapshot of the prior environment. Keep old workers stopped, enable bounded
   execution deliberately, inspect backlog/attempt usage, and retain source and
   backup until retention/review permits removal. Do not erase the original as
   part of this acceptance task.

Example checks on the authorized restore target (read-only):

```sql
SELECT "MigrationId" FROM system.__ef_migrations_history ORDER BY "MigrationId";
SELECT status, count(*) FROM research.research_runs GROUP BY status;
SELECT status, count(*) FROM system.jobs GROUP BY status;
SELECT count(*) FROM content.archive_document_versions;
SELECT count(*) FROM knowledge.finding_evidence;
SELECT count(*) FROM knowledge.finding_evidence e
LEFT JOIN content.document_versions v ON v.id = e.document_version_id
WHERE v.id IS NULL;
SELECT count(*) AS depth, min(created_at) AS oldest_queued
FROM system.jobs WHERE job_type = 'research_plan' AND status = 'pending';
SELECT count(*) FROM system.jobs
WHERE status = 'processing' AND locked_until > clock_timestamp();
```

## Read-only Render inspection: 2026-10-29

The BolyarCoders connector resolves to the previously confirmed My Workspace,
tea-db7tmovavr4c73dv8nog. API srv-dbg4cluc6qcc73ea9qs0
is unsuspended, Free/Frankfurt, configured for main with auto-deploy off. Latest
deploy dep-dbg4cmec6qcc73ea9seg is live at **a6716f7**, not the local P1–P5 code.
Public checks on https://orionsearch-api.onrender.com returned /health 200,
/health/ready 200, /archives 401 and /openapi/v1.json 404.

Database dpg-dbg4c5uc6qcc73ea8590-a is available/unsuspended, PostgreSQL 18, Free,
Frankfurt, with an empty IP allowlist and expiry 2026-11-26 06:35:04 UTC.
The connector's read-only SQL operation was refused by that private access policy;
the allowlist was not relaxed. Current hosted schema/history/row counts are thus
UNVERIFIED in this pass. The earlier eight-migration record is historical.
No service variables, deployment, schema, account, database/network rule or paid
resource were changed. Health/readiness does not prove configured Identity,
worker, S3, synthesis or complete product readiness.

## Release gates still open

- Real research producer/synthesis and bounded whole-discovery HTTP bodies/run
  lifetime (the P3 article capture process is bounded; the earlier Gemini/feed
  pipeline is a separate gate). No live provider call was made here.
- Private hosted object storage/backup policy, runtime/migration credential roles,
  encrypted API key persistence certificate and reviewed account activation.
- Approved always-on/manual execution model within the actual free-plan budget;
  no paid worker is provisioned automatically.
- Frontend authentication/polling UX and correct exact CORS/proxy settings for it.
- Hosted migration, real authenticated success/failure/isolation flow, measured
  production-size backup/restore, retention, audit and revocation reconciliation.
- Named acceptance/security reviewer and verified existing required-check
  enforcement; no CI/CD workflow or release approval is fabricated by this task.

P5 can record local evidence and remove verified blockers. It cannot be marked
fully accepted while mandatory hosted/provider/human gates remain UNVERIFIED.

## Local verification evidence

- PASS: local PostgreSQL 18 empty database upgraded through all 11 migrations; EF reports no pending model changes.
- PASS: API and executor Docker images built; both specify non-root user 1654. API container with local PostgreSQL returned readiness 200, registered and authenticated an account, created an archive, rejected a 20 KB chunked research request with 413, accepted five jobs and returned 429 for the sixth.
- PASS: local PostgreSQL and versioned object-storage restore drill, including encrypted key recovery, revoked old sessions, fenced old leases, recovery and source preservation (see automated restore test).
- UNVERIFIED: hosted migration, end-to-end research and recovery, real provider and release gates listed above. Container smoke used Development and synthetic credentials; no hosted data was modified.
- PASS: `dotnet test services/api/OrionSearch.sln --configuration Release`: 454 passed, 0 failed (2026-10-29). `dotnet format --verify-no-changes --no-restore` passed.
- PASS: PostgreSQL 18 targeted limits, restore and migration suite: 33 passed, 0 failed.
