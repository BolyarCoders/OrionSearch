
## Research Worker Architecture

### Fast Research Mode

The research worker uses a low-latency research pipeline.

1. The user's topic is used directly as the search query.
2. Sources are selected using RESEARCH_SOURCES.
3. Sources are queried concurrently.
4. Articles are filtered by publication date and deduplicated.
5. The newest max_articles articles are analyzed by Gemini.
6. The report and selected articles are returned through the API.

Gemini-based query planning is intentionally disabled in Fast Mode.
This prioritizes response speed over search coverage and diversity.

Default sources:
- google_news
- hacker_news

RESEARCH_SOURCES may contain:
google_news,hacker_news,arxiv,hugging_face

Invalid or empty explicit configurations raise ValueError.

### Persistence

File-based JSON and Markdown persistence was intentionally removed.

The research worker does not persist completed reports or raw
collection results to the local filesystem.

POST /research returns a ResearchResult object.

POST /research/jobs uses an in-memory job store.
Job states and results are lost when the process restarts.

Persistent research history is planned for the main backend
using PostgreSQL. It is not implemented by this PR.

The interactive CLI chat remains available, with conversation
history stored only in memory.

### Response Semantics

total_articles:
Number of unique articles matching the publication date window,
before the max_articles limit is applied.

articles:
The selected articles returned to the client, limited by
max_articles. These are also the articles used for AI analysis.

errors:
Collection errors keyed by source and query/task identifier.
Partial source failures do not necessarily prevent a report.

The worker does not perform full article verification and
analyzes feed metadata and summaries rather than full texts.
