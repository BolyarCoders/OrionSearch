# OrionSearch Research Worker

## Topic-independent discovery (default)

The worker can search for **any public topic**, not only AI/technology:
fashion, agriculture, medicine, vehicles, literature, and more.

The default `RESEARCH_SOURCES` is `web_search,google_news`.
`web_search` searches general web indexes through the `ddgs`
metasearch client, not a fixed list of sites. A valid
`BRAVE_SEARCH_API_KEY` switches web discovery to the official Brave
Web Search API, with a metasearch fallback on provider network errors.
Use an official search API for production reliability: public metasearch
backends may throttle, fail or change their behavior without notice.

`google_news` adds query-based topical news results. Specialized
`hacker_news`, `arxiv` and `hugging_face` are still available for
explicit opt-in, but **are not defaults**. The two RSS adapters for
arXiv and Hugging Face retrieve fixed AI feeds, so do not enable them
for arbitrary topics.

The integrated `POST /research` endpoint:
1. Translates the research objective into an English search query with Gemini.
2. Also searches using the original-language objective (for example Bulgarian).
3. Runs up to three queries against the chosen adapters concurrently.
4. Keeps general web results in search-index relevance order; sorts dated RSS
   results by recency and deduplicates titles.
5. Accepts undated *web discoveries* without inventing publication dates.
   Dated feed results still use the existing rolling date validation.
6. Enforces domain exclusions **before** optional AI language classification.
7. Preserves valid results if another provider fails. An entirely failed
   collection still fails the request.
8. Returns versioned `orion.research.v1` discovery and optionally a Gemini
   report, without changing the .NET API contract or its job ownership.

The integrated endpoint no longer enforces English-only results by default,
so searches can include Bulgarian and other languages. To restore the legacy
filter, set `RESEARCH_ENGLISH_ONLY=true`; explicit archive language/date
settings remain governed by the existing v1 integration behavior.

### Configuration

```env
GEMINI_API_KEY=your_key
ORION_WORKER_API_KEY=your_service_key
RESEARCH_SOURCES=web_search,google_news
RESEARCH_FEED_LIMIT=12
# Optional official provider; without it web_search uses ddgs.
BRAVE_SEARCH_API_KEY=
# Optional legacy filter:
RESEARCH_ENGLISH_ONLY=false
```

`RESEARCH_SOURCES` can contain
`web_search,google_news,hacker_news,arxiv,hugging_face`.
Invalid or blank explicit settings fail fast.

Run `python -m pip install -r requirements-dev.txt` and
`python -m pytest tests agent/tests -q` from `services/research-worker`.
Network calls are mocked in discovery regression tests. Run a separate
authorized live smoke test of the deployed worker before switching the
production endpoint.

### API behavior and persistence

`POST /research` is the versioned, service-to-service endpoint
authenticated by `Authorization: Bearer`; the .NET backend owns
authorization and the persistent queue. The prototype route
`POST /prototype/research` is opt-in and uses `X-API-Key`.
The prototype asynchronous jobs are in-memory and vanish on restart.

The worker does not store research reports as local JSON/Markdown files.
It uses search snippets and feed summaries for analysis by default;
full-text verification is not guaranteed. For evidence-quality research,
enable and verify the existing capture pipeline separately.

`total_articles` in the prototype response counts unique eligible results
before its output limit. `errors` records source collection failures,
which do not necessarily invalidate results from other sources.


### Relevance filtering (research source quality)

Search engines return candidate URLs, not guaranteed on-topic research
sources. Both integrated and prototype worker flows now run a semantic
relevance check against the original research objective and the search
queries **before** committing sources or generating an AI report.

Gemini assigns 0 (unrelated), 1 (tangential), 2 (directly relevant), or
3 (strongly relevant) using the title and search snippet. Only scores 2
and 3 are kept. The best evidence appears first. If the semantic model
fails or returns malformed output, a conservative query-term fallback
rejects unmatched pages rather than persisting obviously unrelated
results. The fallback may miss synonyms or translations; a working
semantic provider is preferable.

For example, searching `electric vehicles` excludes unrelated
`Google Civic Information API` and `Geocoding` documentation. Titles
alone or keywords do not guarantee evidence quality: the worker still
needs capture/content validation for stronger factual verification.
Filtering applies to **new research runs**. Existing archived sources
are not removed or rewritten by this worker change.

### Event-focused news and exact publication dates

The integrated worker now reads `settings.dateFrom` and `settings.dateTo`
as **inclusive publication date boundaries**. When neither is supplied, it
defaults to the last 90 days. It accepts historical windows with a bounded
10-year lookback. Future-only or reversed periods are rejected.

To avoid outdated encyclopedias in news-oriented archives:
- `news_search` uses the public DDGS news index for dated reporting, or
  the official Brave **News Search API** when `BRAVE_SEARCH_API_KEY` is set.
- The AI search planner expands the user topic into concrete event queries,
  such as named product launches and announcements.
- News with genuine publication dates comes before generic undated web pages.
- The final source list is filtered **after** optional HTML publication-date
  enrichment, retaining only dates within the user-selected interval.
- Articles with unknown publication dates are excluded rather than
  presented as recent. Brave's index `page_age` is not treated as a
  publication date because it can reflect page updates.
- The response now includes `relevance` scores for scored sources.

The new default `RESEARCH_SOURCES` is
`news_search,google_news,web_search`; explicit Render environment
variables override this default. Use the same string in Render.

**Reliability:** public metasearch endpoints and Google News RSS can
fail or rate-limit requests, especially from shared/cloud IPs.
For a stable production news provider, configure a valid
`BRAVE_SEARCH_API_KEY`. It supplies a dedicated news endpoint with
supported date filtering; the official service may have costs or usage
limits. Even with a news API, exact publication dates must be
independently established. Some valid news pages may be excluded
when their date metadata cannot be verified.

The 20/50-source response cap remains controlled by archive config;
the worker will not fabricate extra articles to reach a target count.


### Automatic research briefings

When the .NET backend requests a report (now enabled by default through
`ResearchWorker__IncludeReport=true`), the worker produces **one
Bulgarian-language editorial briefing** about all selected sources.

- The overview groups specific developments into thematic trends, discusses
  what changed and why it matters, and identifies uncertain or conflicting
  claims. It is not just a link list.
- It uses the same source order as the stored archive, with [1], [2] citations
  and a mechanically appended bibliography containing every accepted URL.
- It retrieves up to 20 article excerpts (max 2,400 characters each) with the
  existing isolated public fetcher. All other accepted sources contribute their
  publisher/search snippets and are tagged as limited evidence. Failed fetches
  never remove otherwise valid articles.
- For more than eight sources, Gemini summarizes batches and then synthesizes
  a single editorial report from the complete set of numbered source notes.
- An empty result produces a clear no-news statement rather than fabricating
  trends.

The .NET API saves the report in the existing PostgreSQL `research_runs.report`
column. Authorized archive members can retrieve the saved report with
`GET /research-jobs/{runId}/report` after completion. The repository currently
contains no implemented web frontend (`apps/web` is only a placeholder); the
frontend must call this endpoint to show the report in the archive.

The report is an AI synthesis of available evidence, not independent
fact-checking. Source text is untrusted; the model must not follow embedded
instructions. Set `ResearchWorker__IncludeReport=false` on the .NET service
to opt out for old workers. **Do not set this variable on the Python worker:
it is a .NET dispatch setting.**
