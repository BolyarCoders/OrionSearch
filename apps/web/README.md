# OrionSearch web

Frontend implementation of the HTTP behavior in the supplied **API and Backend
Guide (2026-10-20)**. All work is scoped to `apps/web` on `feature/frontend`.

```sh
npm ci
npm run dev
npm run typecheck
npm test
npm run build
```

The web app runs at localhost:3000. Copy `.env.example` to `.env.local` to select
an API origin before starting/building; it defaults to the project's Render API.
No secret, identity UUID or worker key belongs in frontend configuration.

## Guide coverage

| Guide area | Frontend implementation |
| --- | --- |
| HTTP conventions / errors | Typed client, JSON bodies (including `{}` for default research), 204 handling, ProblemDetails fields/trace ID, explicit 400/401/403/404/409/500/503 states, bounded requests and no automatic mutation retry |
| Authentication / permissions | Honest 401 notice and explicit mode switch. Owner lifecycle controls, owner/editor editing and research, read-only viewer settings; database permissions remain the final authority |
| Health / readiness / OpenAPI | Settings page checks process liveness and database connectivity separately and links diagnostic endpoints; explains Development-only OpenAPI |
| Create archive | Title, description, optional UTC dates, all ISO 639-1 language codes, DNS/IDNA domains, duplicate/overlap/100-item validation, opaque JSON config with UTF-8 size validation, preview and templates |
| List archives | Topic/status/include-archived filters, explicit paging/page size, empty/loading/error states. Optional display sorting applies to the visible page; the server pages by creation date |
| Archive detail / PATCH | Metadata/title and full research settings; only changed properties are sent. Explicit null clears description/dates. Edited arrays and config replace their entire field; unedited settings are omitted |
| Soft archive | DELETE preserves data; owner pause/activate/unarchive uses PATCH status |
| Sources | Archive-scoped search, publication-from/to, language and exact publisher domain, all six sorts, pagination/page size, nullable metadata, UTC publication dates and canonical external links |
| Research submission / idempotency | User-initiated submission with one stable request/key across uncertain retries. Pending requests remembered per archive and mode when sessionStorage is available; separate requests require an explicit action |
| Research status | Serial polling without overlapping requests, retry backoff after transient read errors, visible failures, stop on terminal/authorization errors, retry status lookup, all five states, attempts/eligibility/statistics and refresh after completion |
| Frontend integration walkthrough | Create → submit → track public run ID → refresh archive/sources. Restore an explicitly remembered ID or enter a known ID; no fabricated global history |
| Internal execution / Python / ingestion / database | Represented through documented public status/statistics only. No frontend worker, capture ingestion, migration, privileged database access or arbitrary result submission |
| Later capabilities | No invented login, global run/source list, cancel/retry endpoint, findings reader, downloads, scheduling, semantic search or magazines. Unsupported navigation is marked as planned |

## Modes and current integration boundary

**Demo:** in-memory sample archives and a simulated research lifecycle. Data
resets on a full page reload. Example navigation/activity is labeled and appears
only in Demo. Sample archive counts are presentation fixtures; tracked demo runs
report their actual simulated output counts.

**Live API:** genuine HTTP calls to `NEXT_PUBLIC_API_URL`; no silent mock
fallback. Changing mode remounts view state so demo results cannot be shown as
live results. Current documented backend authentication is unconfigured, so
protected requests can return 401. Browser CORS must also allow the frontend's
actual origin; a direct health URL working does not prove browser fetch access.
The UI never sends caller identity headers or pretends a login endpoint exists.

Actual authenticated research acceptance still requires a real identity provider,
a compatible Python service and an enabled backend worker. These are backend
integration dependencies; this frontend change does not implement or enable them.

## Validation

Contract tests cover sparse PATCH/null behavior, source query parameters,
normalization/config limits, idempotent demo submission, bodyless DELETE,
ProblemDetails and no live/demo data fallback. Component/hook tests cover stable
submission retries, serial/terminal polling, unmount cleanup and role restrictions.
Browser smoke checks exercise the visible archive/settings/sources/research flow
at desktop and mobile widths. This project has no separate ESLint configuration;
TypeScript and the Next production build are the configured static gates.

## API implementation verification — 2026-10-27 (before redesign)

| Gate | Result | Evidence |
| --- | --- | --- |
| Full frontend test suite | PASS | `npm test -- --reporter=dot --maxWorkers=2`: 58 passed across six suites |
| TypeScript, including tests | PASS | `npm run typecheck` |
| Production artifact | PASS | `npm run build`: all routes compiled, typed and packaged |
| Production browser flows | PASS | Archive list/search, include archived, source filters, metadata/languages PATCH, research completion, creation/config, pause/activate, archive/unarchive, health/readiness |
| Loading and authentication error views | PASS | Controlled browser HTTP fixture exercises a delayed response and 401/trace ID; live views contain no demo archives |
| Responsive / visual review | PASS for inspected states | Rendered 1440 px desktop and 390 px mobile; no document horizontal overflow or browser exceptions after the topbar/tab corrections |
| Real authenticated backend/agent acceptance | UNVERIFIED | The documented provider and compatible worker remain backend integration dependencies |

Rendered evidence: [dashboard](qa/guide-dashboard.png), [empty](qa/guide-empty.png),
[loading](qa/guide-loading.png), [sources](qa/guide-sources.png),
[research](qa/guide-research.png), [create](qa/guide-create.png),
[mobile sources](qa/guide-mobile-sources.png), [401 fixture](qa/guide-live-error.png).
These historical screenshots show the previous cosmic-art direction. The current
research-library design and verification are documented in [design review](docs/design-review.md).
Forms/filters are extended to cover the public contracts. Mobile tabs and tables
scroll inside their own containers; unsupported product areas remain marked as
planned. This is rendered implementation review, not a claim of new human design
approval or live-provider acceptance.

The work remains local in `feature/frontend` with the pre-existing untracked
frontend files. No backend changes, new branch, commit, push or deployment was
performed for this frontend implementation.

## Research-library redesign — 2026-10-27

The current frontend uses text-led dossier cards, quieter source metadata,
functional navigation, system light/dark appearance and accessible contrast.
Mobile creation uses a compact step indicator. See the [design review and token plan](docs/design-review.md)
for the audit, HIG citations, rationale, rendered comparisons and verification gates.

Verification: 58/58 tests; TypeScript; final production build; production browser
checks at 1440/768/390/320px and 200% text, including drawer/search keyboard behavior
and loading/401 fixtures. Demo workflow checks cover creation, settings, source
filters, lifecycle and research. Human design approval and real authenticated
backend acceptance remain unverified.

Preview: `npm run start -- --port 3000` after `npm run build`.
Current screenshots: [light](qa/design-final-light.png), [dark](qa/design-final-dark.png),
[mobile](qa/design-final-mobile.png), [archive](qa/design-detail.png),
[creation](qa/design-create-layout.png).
