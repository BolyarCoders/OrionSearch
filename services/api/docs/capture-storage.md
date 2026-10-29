# Capture storage and integrity (P3)

Implementation and operations, 29 October 2026. Local development only; no hosted store
or credentials are provisioned by this change.

| Architecture question | Decision |
| --- | --- |
| Source of truth | Private S3-compatible versioned objects own raw bytes; PostgreSQL owns accepted text, chunks, evidence and archive/version access. |
| Ownership | Python uploads into a run-scoped prefix with a fresh execution nonce. Only .NET accepts references. Python has no database credentials. |
| Dependencies | Application defines ICaptureStore. Infrastructure implements bounded S3 reads. Domain remains storage-SDK independent. |
| Boundary | Existing raw.storageUri carries a strict s3 bucket/key plus a required immutable versionId; raw.sha256 is checked against streamed bytes. No credentials, arbitrary URLs or redirects are dereferenced. |
| Failure semantics | Invalid reference, oversized data or digest mismatch is permanent contract failure; storage outage is retryable. No result rows are committed before verification. Each I/O has a bounded timeout. |
| Lifecycle | Upload precedes acceptance; worker heartbeat covers verification. Lease and archive are rechecked under lock afterward. Failed/stale uploads remain unreferenced; retention is conservative (see orphan policy). |
| Test seams | Injectable S3 client and fetch connection, real local versioned object store, PostgreSQL migrations, cross-archive HTTP checks and corrupt/missing object cases. |
| Compatibility | Wire shape unchanged; raw references are now fail-closed unless the store is configured. Text-only/discovery results remain supported. Additive archive-version grants; old evidence provides safe backfill, unrelated global versions receive no grant. Roll back application without dropping grants; prefer forward fixes. |
| Observability | Existing finite research failure categories and run IDs; capture metadata records verified byte length/media type. No credentials or document bodies in logs. |
| Security | Private bucket, versioned references, run-prefix restriction on acceptance, read-only API credentials, producer PutObject permission without DeleteObject/DeleteObjectVersion. Reads require active user and archive membership plus explicit version grant. Raw downloads use attachment/octet-stream/nosniff. |

Implementation: store interface/options/S3 adapter; pre-transaction queue
verification and heartbeat lifetime; archive-version grant model/migration;
authorized version and download endpoints; bounded public-source Python fetch,
normalization/chunks and versioned upload; tests and operations documentation.

## Invariants and migration

The new archive_document_versions row is an authoritative access grant created
in the same transaction as ingestion. Its composite foreign keys require both
an archive/document link and a version belonging to that exact document.
The primary key serves archive/version reads; the version/document alternate
key supports that FK; a reverse index supports version deletion checks.
Backfill uses only persisted evidence whose finding/run belongs to the archive
and whose document is linked there. Mere global document reuse grants nothing.
No existing version, text, evidence or storage reference is rewritten.
The grant also owns its archive's raw URI/digest. Global extracted-text dedup
does not grant a raw HTML representation accepted by another archive. Legacy
backfill grants text access only: a historical global raw URI cannot prove which
archive accepted those bytes. The API never falls back to that global raw URI.
The additional archive/document index supports cascade FK checks; the raw check
constraint requires URI/digest to be either both absent or both present.

Raw integrity checks attest stored bytes, not the truth of a finding or the
accuracy/completeness of HTML extraction. Python extracts visible article/main
text (body fallback), never feed summaries. Findings remain empty until a
separate synthesis step produces grounded statements; existing exact quote
validation remains authoritative for submitted evidence.

## Orphan policy

Do not apply a blanket bucket TTL: shared accepted versions may outlive a run
or archive. This package deliberately grants no delete permission to the API
or producer. Failed uploads are retained. Before any manual removal, an operator
must stop/drain executors, allow a grace period of at least 7 days, enumerate
object **versions**, and compare exact versioned references against all
both content.document_versions (including legacy rows) and
content.archive_document_versions. Delete only unreferenced
versions, then retain an audit inventory. Referenced versions are never cleanup
candidates, even if their originating archive was archived. Automated deletion
requires a later coordinated tombstone/claim protocol; no unsafe sweeper runs
in the application.

## Configuration and activation

Capture execution and storage are disabled by default. Existing text-only and
discovery results still work. This change does not enable the worker, modify
Render, provision a provider or incur provider/LLM charges.

Choose a private S3-compatible bucket with object versioning **enabled before
uploads**, TLS, retained versions, no public ACL/policy and no blanket expiry.
The deployment provider/region/quotas and backup policy require operational
selection; the local test store is not a hosting recommendation.

API environment variables (configure secrets through the hosting secret store):

```text
CaptureStore__Enabled=true
CaptureStore__Endpoint=https://<trusted-s3-endpoint>
CaptureStore__Region=<region>
CaptureStore__Bucket=<private-versioned-bucket>
CaptureStore__Prefix=captures
CaptureStore__AccessKey=<read-only-key>
CaptureStore__SecretKey=<read-only-secret>
```

The API credential needs GetObjectVersion on the capture prefix; it needs no
PutObject, DeleteObject, DeleteObjectVersion or public-link permission. Keep
storage HTTP diagnostics/body logging off. The endpoint is server configuration,
never research input. Explicit AllowLoopbackHttp is for isolated local tests
only. Default read bound: 2 MiB/object, 30 seconds; limits permit 1 KiB–10 MiB
and 1–120 seconds. A complete ingestion verification budget is 60 seconds and
16 MiB across raw objects. SDK retries are disabled; the durable queue owns retry.

Python environment variables:

```text
ORION_CAPTURE_ENABLED=true
ORION_CAPTURE_ENDPOINT=https://<trusted-s3-endpoint>
ORION_CAPTURE_REGION=<region>
ORION_CAPTURE_BUCKET=<same-private-versioned-bucket>
ORION_CAPTURE_PREFIX=captures
ORION_CAPTURE_ACCESS_KEY=<upload-only-key>
ORION_CAPTURE_SECRET_KEY=<upload-only-secret>
```

Give the producer PutObject only under captures/*; deny object/version deletion
and bucket-policy/versioning changes. This implementation uses static scoped
service credentials; fresh execution nonce directories isolate uploads, but
per-attempt temporary IAM credentials are not minted. The API checks the run
prefix independently. A future credential broker may narrow producer permissions
per invocation. Service-key rotation remains independent of storage credentials.
The producer verifies a non-null version ID on upload. Unsupported/unversioned
stores fail; there is no mutable-object fallback.

Raw reference form (wire schema unchanged):

```text
s3://<bucket>/captures/<run-uuid>/<execution-uuid-N>/<sha256>?versionId=<URI-escaped-version>
```

Only that bucket/prefix/form is accepted, with an exact run match at ingestion.
Every read names the immutable object version and recomputes SHA-256; ETag is
not treated as a content digest. No source-provided https/gs/urn locator is
dereferenced. Missing/corrupt/oversized objects reject ingestion before any DB
transaction; service/storage outages retry through the existing finite policy.
Heartbeat runs during verification. A final live-lease/archive check still gates
the atomic ingestion, grant creation, evidence and job completion transaction.

## Retrieval limits and truthful results

The optional capture stage considers at most the first 10 discoveries, at most
2 MiB raw bytes per page and 16 MiB total. It accepts text/html or UTF-8
text/plain; compressed responses, PDFs, empty/unreadable text and extracted
text over 16000 Unicode scalar values remain discoveries without captures.
It never substitutes a feed description. HTML script/style/template/navigation
elements are removed; article/main is preferred with body fallback. This is
visible text extraction, not a guarantee that every page is a complete article.
Normalization is NFC, .NET-compatible LF line endings and Unicode whitespace
trim. Deterministic chunks contain up to 2000 scalar values, with exact offsets.

Each URL and redirect is canonicalized, resolved and checked against excluded
domains; every DNS answer must be globally routable. The transport pins one
validated address while TLS verifies the original hostname, ignores proxy
environment settings, sends no cookies/authorization and disables decompression.
Only standard ports, at most 3 redirects and no HTTPS downgrade are allowed.
A disposable process caps each retrieval (including DNS) at 15 seconds, within
a 60-second retrieval-stage budget; it is terminated on timeout. Uploads have
5-second connect/10-second read limits, one SDK attempt, no unbounded retry.
Those per-operation limits do not imply a hard deadline for the entire existing
Gemini/feed discovery pipeline. That earlier adapter hardening/hosted gate stays
open; .NET cancellation/fencing remains the durable acceptance boundary.

Rejected or unavailable articles remain discoveries; upload/store errors fail the
run rather than hiding a broken store as empty capture success. No findings are
invented from the fetched body. Synthesis and real-provider product acceptance
remain separate work. Submitted findings must still pass exact quote/chunk/run
checks in .NET and retain the unverified status.

## Read endpoints

All routes require active user and owner/editor/viewer archive membership.
Nonmembers get 404. Raw paths, bucket names and secrets are omitted from DTOs.

| Route | Result |
| --- | --- |
| GET /archives/{archiveId}/sources/{documentId}/versions | Page of accepted IDs/fetchedAt/hasText/hasRaw, defaults page=1/pageSize=20, max 100; fetchedAt descending then id ascending. A linked discovery returns an empty page. |
| GET /archives/{archiveId}/sources/{documentId}/versions/{versionId} | Normalized text, content hash and ordered chunks for an explicitly granted version. |
| GET /archives/{archiveId}/sources/{documentId}/versions/{versionId}/raw | Reverified bytes as application/octet-stream attachment, no-store, nosniff and sandbox CSP. Missing grant/raw=404; unavailable/corrupt storage=generic 503. |

No signed public URLs, arbitrary remote-fetch endpoint, inline HTML rendering or
global version browsing is added. Archive pause/archive preserves existing read
permissions consistently with source listing; it does not delete shared captures.

## Verification

Checks below use branch feature/backend-integration after commit 05d5e3f.
The suite uses a disposable SeaweedFS 4.51 container with real versioned S3
uploads/reads and an isolated PostgreSQL. Its local test-admin credential can
replace/delete objects to simulate damage; production components receive no
such deletion permission. No hosted or paid service is used.

| Gate | Evidence |
| --- | --- |
| Full .NET backend suite | PASS: `dotnet test services/api/OrionSearch.sln --configuration Release`, 416 passed, zero failed/skipped; p3-backend.trx, PostgreSQL 16 + real local versioned S3. |
| PostgreSQL 18 / final store fixture | PASS: ORIONSEARCH_TEST_POSTGRES_IMAGE=postgres:18-alpine, migration/upgrade/constraint and real S3 integration filter, 19 passed, zero failed/skipped; p3-postgres18.trx. The fixture permits 16 sparse volumes so default and test-bucket collections cannot race for two startup slots. |
| Python full suite | PASS: `.venv/Scripts/python.exe -m pytest tests agent/tests -q`, 96 passed. One existing Starlette/httpx deprecation warning remains. |
| Byte/Unicode contract | PASS: Python reproduces capture-result.json from actual capture-source.html bytes; .NET validates its raw digest, normalized text and scalar offsets. |
| Cross-language object I/O | PASS: actual Python CaptureUploader/Boto3 uploaded 92 synthetic HTML bytes to a private local versioned bucket; actual .NET S3CaptureStore/AWS SDK read that immutable version and recomputed the matching digest. The provider input was a fixture, not live Gemini/article acceptance. |
| Linux packaging | PASS: worker Docker build, image sha256:71e0888723d6dcd03d060a9be81949b27e3690dead05c0524ebdb166fa8d2a64; smoke imported the actual app/schema/SDK, extracted the shared fixture and exercised a spawned retrieval process as UID 10001. |
| Build/model/dependencies | PASS: Release API publish; EF has-pending-model-changes reports no changes; Python pip check succeeds; NuGet vulnerable/transitive report finds no listed vulnerabilities for Infrastructure at check time. |
| Format/documentation | PASS: dotnet format --verify-no-changes --no-restore; git diff --check; updated local Markdown links resolve. |
| Hosted/product acceptance | UNVERIFIED: provider selection and private bucket policies/quotas/backup, live Gemini/source retrieval, Render activation, full authenticated hosted flow and restore drill. No hosted schema or feature flag was changed. |

Temporary cross-language probe/build logs stay outside the repository. No fake
storage or provider fallback is registered by production startup. The historical
heartbeat ordering test now verifies that renewal stays active through object
verification and is settled before the processor returns; it does not require
stopping renewal before entering completion. Separate regression tests verify
that no DB transaction spans verification, stale attempts cannot ingest after
reassignment, and renewal continues while verification is held behind a barrier.
