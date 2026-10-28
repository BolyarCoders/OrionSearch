# OrionSearch

**Autonomous research and knowledge archiving.**  
*Discover. Preserve. Understand.*

OrionSearch is an AI-powered platform designed to continuously collect and organize information about topics you follow, preserve the sources behind it, and turn the resulting archive into readable, citation-backed digital magazines.

**Project status:** In development · Hackathon MVP

## Why OrionSearch

Information about fast-moving topics is scattered across news outlets, websites, and reports. Repeatedly searching, comparing coverage, and remembering what changed takes time. OrionSearch aims to maintain a reusable, evolving knowledge archive instead of producing only a one-off answer.

> The archive is the product. The magazine is one way to explore it.

## Planned capabilities

These are product goals; not all capabilities are implemented yet.

- **Topic archives:** Organize research around topics, industries, technologies, or events.
- **Automated research:** Discover sources and revisit topics on a configurable schedule.
- **Traceable evidence:** Preserve source links and available publication and retrieval dates.
- **Incremental updates:** Add newly discovered material while retaining research history.
- **Knowledge exploration:** Search and browse saved sources, related coverage, and changes over time.
- **AI-generated magazines:** Create readable, structured editions with citations to underlying evidence.

## How it works

1. Configure a topic archive and its research preferences.
2. Discover and retrieve relevant public sources.
3. Analyze and organize evidence while preserving source links.
4. Store results so future research can build on previous runs.
5. Generate a magazine-style view from the accumulated archive.

## Planned architecture

| Component | Planned technology | Responsibility |
|---|---|---|
| Web client | Next.js, React, TypeScript | Archive management, search, and magazine reading |
| API | C#, ASP.NET Core (.NET 10) | HTTP API and application orchestration |
| Research workflow | LangGraph and a configurable LLM | Research planning, source analysis, and synthesis |
| Data store | PostgreSQL, Entity Framework Core, Npgsql | Archives, sources, findings, and research history |
| Background processing | .NET worker with a PostgreSQL queue; recurring scheduler to be decided | Leased research execution and future recurring jobs |
| Infrastructure | Docker, Docker Compose, GitHub Actions | Local environment, CI/CD, and deployment |

The implementation may evolve as the team builds the MVP. AI and search providers should remain configurable, and generated claims should retain links to their supporting sources.

## Development status

The ASP.NET Core API includes archive management, metadata topic/date filtering, EF Core migrations and queued research execution; see [the backend README](services/api/README.md) for setup and integration prerequisites. Native ASP.NET Core Identity accounts are implemented behind explicit configuration; email/password registration, immediate login, refresh and password changes are described in [Native Identity](services/api/docs/native-identity.md). No confirmation email or external identity provider is required. Public account activation, frontend integration and the AI team's agreed research contract remain pending. Default startup rejects protected requests and leaves research processing disabled. The [Render hosting record](services/api/docs/render-hosting.md) documents the API/database deployment separately from product readiness.

Never commit secrets, API keys, local `.env` files, or user data.

## Research worker prototype

The current branch includes a prototype research worker with four public-source adapters (Google News RSS, arXiv RSS, the Hacker News Algolia API, and the Hugging Face blog feed), article filtering/deduplication, Gemini-assisted research planning and analysis, Markdown/JSON report output, and a FastAPI interface for synchronous research and background-job status.

This is not yet the complete planned architecture: the worker does not use LangGraph and is not integrated with PostgreSQL or a finalized shared backend job contract. The collector processes feed metadata and summaries rather than verifying full article text.

### Install and run tests

From the repository root:

```bash
python -m venv .venv
# Activate the virtual environment for your shell, then:
python -m pip install -r services/research-worker/requirements-dev.txt
cd services/research-worker
python -m pytest tests agent/tests -q
```

The tests mock external services and should not require live source APIs or a Gemini key. Generated test output should use temporary directories rather than the repository's output folder.

### Run the worker API locally

From `services/research-worker`, set `ORION_WORKER_API_KEY` and `GEMINI_API_KEY` in your environment (or a local ignored `.env` file), then run:

```bash
python -m uvicorn api.main:app --host 127.0.0.1 --port 8000
```

The API exposes `GET /health`, `POST /research`, `POST /research/jobs`, and `GET /research/jobs/{job_id}`. Research endpoints require the `X-API-Key` header. Background-job state is currently in memory and is lost when the process restarts.

Generated JSON and Markdown reports are written under `services/research-worker/agent/output/`. These are local run artifacts and must not be committed. The output directory is ignored by Git.

## Contributing

- Create a feature branch for each Jira issue.
- Keep pull requests focused and link the relevant `ORION-...` issue.
- Add or update tests for changed behavior.
- Once CI checks are configured, do not merge failing checks.
- Document new configuration fields and service dependencies.

## License

The license has not yet been selected. No open-source license is implied until a `LICENSE` file is added.

---

*OrionSearch — Knowledge that grows with time.*
