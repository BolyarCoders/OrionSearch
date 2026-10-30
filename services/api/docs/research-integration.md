# Research integration: P2 architecture and execution plan

## AI report integration

The live worker at `https://orionsearch-409q.onrender.com` currently serves the
versioned Bearer-authenticated `/research` contract. The earlier handoff's
`X-API-Key` job endpoints describe the prototype, which is disabled by default
and does not provide a durable queue. The .NET PostgreSQL queue remains the
authoritative owner of jobs, retries and completion. The worker produces output
but never writes application data. User tokens and database credentials do not
cross this boundary.

When `ResearchWorker__IncludeReport=true`, the .NET executor requests a report
from a compatible worker. The worker filters and deduplicates sources first,
then supplies those same ordered sources and feed summaries to the AI analyst.
The report and the other accepted results are committed in the existing fenced
transaction. Archive members can retrieve it with
`GET /research-jobs/{runId}/report`; unavailable or unauthorized reports return
404. The text is an unverified AI analysis of metadata and feed summaries, not
captured full text, evidence, or independently verified facts. Clients should
render it as text, not trusted HTML. Existing runs can have no report.

This extends v1 with optional `includeReport` and `report` fields. A producer
only sends a report on explicit request. Old producers reject the new request,
and old consumers reject the new response: keep the option off until both
upgrades are deployed. Provisional requests do not request reports. Existing
requests, retries, idempotency and API status responses are unchanged.

Rollout: back up the database and apply the additive report migration before
deploying the new .NET consumer; deploy the compatible Python producer; configure
the same service key as `ORION_WORKER_API_KEY` in Python and
`ResearchWorker__AgentApiKey` in .NET, set `ResearchWorker__AgentBaseAddress` to
the worker origin, and only after testing set `ResearchWorker__IncludeReport`
and `ResearchWorker__Enabled` to `true`. Keys belong in server-side secrets.
Never enable prototype routes for this integration. In case of rollback, turn
report requests off before reverting the producer. The migration refuses a
downgrade that would delete stored reports; retain the column or back up and
plan an explicit data migration. The HTTP client has finite response and time
limits, the PostgreSQL lease fences late results, and failures use sanitized
categories and bounded retries. Provider output, including reports, is untrusted.
No new paid service or shared database credential is required.

The current Free Render web instance can sleep while idle. An in-process queue
executor therefore cannot guarantee prompt execution while the API is asleep;
a reliably running executor requires a separately assessed hosting option. A
successful `/health` check only establishes that the worker process responds;
it does not prove Gemini, feeds, credentials or report generation are working.
An authenticated end-to-end run is required before enabling production traffic.

On 2026-10-30, live diagnostics identified a Google News 503 from the Render
worker and a Gemini `gemini-3.8-flash` 504. The worker was temporarily configured
to use only `hacker_news`. A `gemini-2.5-flash-lite` fallback returned 404, so
`gemini-3.5-flash-lite` was configured as fallback. An authenticated hosted
request returned three Hacker News sources and a generated report after the
primary model timed out; full API-to-database acceptance remains separate.
This reduces source coverage. Restore additional sources only after verifying
their availability; the
integrated route currently fails the run on any source error rather than
silently publishing an incomplete report. Feed queries use the research
objective; the title and description are context for report generation.

Candidate implementation, 2026-10-28. This step joins the existing .NET consumer
and Python producer; hosted/real-provider acceptance remains a separate gate.

P3 update (2026-10-29): optional captures and verified storage are now implemented
in [capture storage](capture-storage.md). The P2 discovery behavior and results
below remain the historical baseline; captures are explicitly configured, not
automatically enabled by the P2 wire integration.

| Architecture question | Decision |
| --- | --- |
| Source of truth | PostgreSQL owns runs, attempts, leases and accepted results. Python produces a response, never a second authoritative job. |
| Ownership | .NET alone writes application data. Python receives no DB credentials and executes bounded source discovery. |
| Dependencies | Shared JSON Schemas live in packages/contracts/research/v1. Python validates against them; .NET uses typed records plus semantic checks. |
| Boundary | POST /research with versioned JSON and a service Bearer credential. No user tokens, queue internals, caller-selected endpoints or credentials from config JSON. |
| Failures | Bounded request/response sizes, nonblocking capacity refusal, finite provider/network timeouts, generic errors and .NET-owned finite retries. No implicit empty success when collection fails. |
| Lifecycle | One synchronous Python execution slot; no in-memory public job lifecycle in integrated mode. Process failure is recovered through the .NET lease. |
| Test seams | Shared valid/invalid fixtures, replaceable source collection/planning in Python tests, HTTP transport seam and real PostgreSQL queue tests in .NET. |
| Compatibility | New submissions use orion.research.v1. Existing provisional payloads require explicit AllowProvisionalContract and exact request/result version matching. No silent payload rewrite. Prototype routes move under /prototype and require explicit opt-in. |
| Observability | Log run IDs and finite failure categories, never research body/provider exception bodies or keys. Document discovery counts separately from verified captures/findings. |
| Security | Constant-time service-key comparison, optional prior key for rotation, no arbitrary article fetching or model-selected tools, no DB access from Python, schema/semantic rejection before work/ingestion. |

Implementation order: shared schemas/fixtures; Python input validation and Bearer
authentication; discovery adapter over the current planner/collector; .NET v1
selection, response correlation and compatible lease completion; cross-language
fixture and failure tests; operations/acceptance documentation.

The current producer discovers URLs and metadata. It does not turn feed summaries
or the prototype Markdown report into full-text captures, evidence or findings.
P3 supplies actual capture/storage verification. A successful P2 result can have
sources with zero versions/findings; counts must remain truthful. No local report
files are written by the integrated route. The standalone CLI/prototype retains
its existing report behavior behind explicit operator opt-in.

The current adapters support a rolling window of at most 90 days. Explicit date
windows outside that capability are rejected rather than silently truncated.
Unknown publication dates do not satisfy explicit date filters; unknown language
does not satisfy an explicit language filter. Excluded domains are removed and
preferred domains rank first. Other opaque archive config cannot change service
credentials, model, filesystem paths or endpoint addresses. maxArticles is the
only execution setting accepted here (1–50, default 20).

The stable contract is an implementation candidate for team review, not a claim
of AI-team approval or a production deployment. This task adds no CI/CD, paid
resource, database migration or automatic worker enablement.

## Implemented behavior and operations

See the [v1 contract](../../../packages/contracts/research/v1/README.md) for exact
shapes, limits, version rollout, failure policy and temporary legacy flags.
The integrated request accepts at most 256 KiB of UTF-8 JSON, including .NET's
escaped Unicode form of maximum legal archive settings. Result identity,
content type, casing, duplicate properties and semantic validation are enforced
before acceptance. Retry-After is carried through the processor into bounded
PostgreSQL retry timing. Existing lease/attempt fencing remains authoritative.

RSS update times and Hacker News discovery times do not become publication dates
of linked articles. Unknown publisher metadata in Google News wrappers cannot
bypass requested domain exclusions. International hostnames use IDNA mapping
consistent with the consumer; source keys are URL identifiers, never content
hashes. These boundaries have regression tests.

Synchronous collector calls are not force-cancelled by disconnecting the .NET
client. The slot remains held until work stops, and Python cannot commit results.
Provider-body limits and hard whole-run retrieval isolation must be completed
with P3/operations hardening before public activation. This is a known deployment
gate, not a claim of a hard Python wall-clock deadline.

## Verification: 2026-10-28

Candidate branch: `feature/backend-integration`, continuing native-account
commit `549085a`. Verification used local isolated resources only.

| Gate | Result / evidence |
| --- | --- |
| Full .NET backend suite | **PASS:** `dotnet test services/api/OrionSearch.sln --configuration Release`, 391 passed, zero failed/skipped, 4m; `p2-backend.trx` |
| Final contract-specific .NET check | **PASS:** `--filter FullyQualifiedName~ResearchContract`, 17 passed, including the final IDNA identity assertion |
| Full Python suite | **PASS:** from services/research-worker, `.venv` Python `-m pytest tests agent/tests -q`, 65 passed; one pre-existing Starlette/httpx deprecation warning |
| Wire integration | **PASS:** actual HttpResearchAgentClient called a separate loopback uvicorn process using the normal Bearer/schema/discovery boundary with fixed upstream-provider input; echoed v1/run ID, one source, zero captures/findings |
| Worker packaging | **PASS:** `docker build -f services/research-worker/Dockerfile -t orionsearch-research-worker:p2 .`; inspected image ID `sha256:1d71d76d09cc041a1699353444f6b12478522655c6125c338a94db2515d8097c` |
| Linux image smoke | **PASS:** image imported the actual app, validated request/result and IDNA normalization, loaded schemas at /app/packages/contracts/research, and executed as UID 10001 |
| Build/format/model | **PASS:** Release API publish, `dotnet format --verify-no-changes --no-restore`, EF `has-pending-model-changes` reports no changes |
| Python dependency compatibility | **PASS:** `pip check` reports no broken requirements |
| Real Gemini / hosted product acceptance | **UNVERIFIED:** no paid provider query, Render deployment, real-source capture or authenticated API→queue→live-agent→storage product acceptance was executed |

TRX/build logs and the temporary socket probe were kept outside the repository.
The probe's Python provider input was a test fixture; it was not a live Gemini
result. The test server was stopped, and production code contains no fake-agent
fallback. No worker, prototype mode or live authentication setting was enabled.

P2 implementation and local compatibility are verified. RB-03/RB-04/RB-05 remain
open for team review, real provider/capture integration and hosted acceptance.
