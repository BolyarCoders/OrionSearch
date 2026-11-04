> Superseded by the research-library redesign in `apps/web/docs/design-review.md` (2026-10-27). This document is retained as the historical cosmic-art direction.

# OrionSearch Frontend — Approved Visual Direction

This pack contains user-approved design references and six standalone topic-cover images for use in `apps/web`.

## Product identity

OrionSearch is a research archive product. Design it as a sophisticated, usable desktop-first web app with the polish of Arthur/Nexus. The approved style is a *disciplined dark SaaS workspace with a restrained purple accent and purposeful editorial archive imagery*. The reference images are **visual targets**, not source code or accurate API mock data.

## Visual system (starting tokens; tune against reference)

- Workspace background: `#0C0E14` with sidebar `#0B0D13`.
- Surfaces: `#111520`, raised/input `#161A26`.
- Borders: `#282D3B` / muted hairlines; little or no shadow.
- Primary text: `#F3F4F8`; secondary: `#A3A8B8`; muted: `#737B8C`.
- Accent: violet `#7056F5` / hover `#826CFF`, only for selection, calls to action, important active controls.
- Success `#46CFA5`, warning `#E6B65A`, failure `#F07581`, info `#678AFF`, with restrained opaque/translucent tags.
- Typeface: Geist or Inter; readable, consistent type scale and spacing. Avoid an all-serif headline or a sci-fi typeface.
- Corners: 8–12 px for panels; 6–8 px for controls; consistent thin borders.
- Layout: 240–256 px sidebar; approximately 56–64 px topbar; content max width ~1440 px; normal scrolling, responsive mobile sidebar.
- Microinteractions: 140–200 ms subtle hover/focus states; no looping full-page glowing animation.

## Approved visual references

- `design/references/01-approved-dashboard.png`: user-approved archive dashboard mood, navigation, stats, filters, archive cards, recent activity.
- `design/references/02-workflow-layout.png`: wider information architecture reference across Dashboard, Create Archive, Archive Detail, and Research/Sources. Some labels and controls in these illustrative mockups are not backed by the API; adapt rather than reproduce them blindly.

## Screens

### 1. Archives Dashboard

- Compact sidebar, utility header, focused page title `Your research archives`.
- Condensed count summary backed by actual list data; never fabricate global aggregate counts or trends (pagination may limit counts).
- Archive grid 3 columns on large desktops, 2 tablet, 1 mobile. Each card uses 16:9 artwork as a shallow cover crop, then title, description, source count, lifecycle status, and `updatedAt` clearly labeled as archive updated (not research completed).
- Topic search, lifecycle filter, pagination; empty/loading/error states.
- Recent research activity is optional *demo-only presentation*: current API does NOT expose list-all-runs. If not truly supported, omit in live mode or show a clearly labeled coming-soon empty state. Never fabricate running history from archive metadata.

### 2. Create Archive

- Same shell, dedicated form with title, description, date window, preferred/excluded DNS domains, languages, and stored frequency preference.
- Form can be a single page with grouped sections or stepped presentation, but don't block quick creation needlessly.
- A restrained live preview uses zero source count and the locally entered title/description; no imaginary research stats.
- Reuse generic default artwork for unmatched topics; the six cover images are bundled examples, not user-uploaded images or API-backed image metadata.
- `title` is the backend topic; no separate `topic` property.
- Store frequency as a documented application preference inside `settings.config` only. The backend does not run scheduled research yet. Label it as a *preferred cadence (scheduling not active)* where relevant.

### 3. Archive Detail / Research & Sources

- Cohesive archive header featuring an understated cropped cover strip (not an oversized landing hero).
- Clear `Overview`, `Sources`, `Research`, `Settings` composition, but ensure each visible tab has meaningful behavior or is explicitly disabled/coming soon.
- Research run status uses `queued / running / completed / failed / cancelled`, statistics (`queries`, `sources`, `versions`, `findings`) and attempts.
- An indeterminate status indicator during running; do not manufacture completion percentages, phases, logs or live source streaming.
- Sources list/table: title, publisher/domain, `publishedAt` where present, `addedAt`, relevance, external canonical URL. Unknown publication dates remain `Unknown`.
- Sorting/filtering/pagination must follow actual backend support. A run does not have a dedicated per-run source listing endpoint; archive sources belong to the archive.

## Live integration constraints (backend guide dated 2026-10-20)

- Actual backend uses ASP.NET Core, not the proposed FastAPI implementation mentioned in older product README.
- Base URL for local API: `http://localhost:5190`, with routes `/archives`, `/archives/{id}`, `/archives/{id}/sources`, `/archives/{archiveId}/research-jobs`, `/research-jobs/{id}`.
- Backend authentication is not configured and protected calls currently return 401 on ordinary startup. Never add insecure trust headers or pretend bearer auth works. Use explicit DEMO and LIVE API modes with no silent mock fallback.
- No automatic scheduler; no public research run list, logs, cancel/retry endpoint, per-run source list, or frontend archive cover field.
- Use shared typed API client, strict response models, accessible components, error states, tests, and responsive layouts.

## Assets

Images are 1672×941 source images converted to WebP with quality 86, names:

- `/images/archives/artificial-intelligence.webp`
- `/images/archives/quantum-computing.webp`
- `/images/archives/climate-policy.webp`
- `/images/archives/space-exploration.webp`
- `/images/archives/electric-vehicles.webp`
- `/images/archives/bioengineering.webp`

Use `next/image` with responsive sizes, CSS `object-fit: cover`, and a dark overlay as needed for consistent readability. Images must remain purely decorative; keep labels as real HTML. Avoid blindly selecting imagery from title substrings that may misclassify arbitrary user-created archives; use a deterministic, documented demo template mapping plus an elegant neutral fallback for all others.

## Implementation acceptance

All routes work from navigation; cards open corresponding archive; form validates and submits; research starts only by user action; polling stops at terminal state; sources table renders properly with missing nullable values; demo data clearly distinguished from live; responsive at 390 px and 1440 px; lint, typecheck, test and build pass; no unrequested backend changes or pushes.
