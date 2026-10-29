# Research integration: P2 architecture and execution plan

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
