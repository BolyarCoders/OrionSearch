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
| API | Python, FastAPI, Pydantic | HTTP API and application orchestration |
| Research workflow | LangGraph and a configurable LLM | Research planning, source analysis, and synthesis |
| Data store | PostgreSQL, Alembic | Archives, sources, findings, and research history |
| Background processing | Worker and scheduler (to be decided) | Long-running and recurring research jobs |
| Infrastructure | Docker, Docker Compose, GitHub Actions | Local environment, CI/CD, and deployment |

The implementation may evolve as the team builds the MVP. AI and search providers should remain configurable, and generated claims should retain links to their supporting sources.

## Development status

The full application and local service stack are still being developed. **There is no supported local run procedure for the complete application yet.** Docker configuration and an example environment file are planned under ORION-14. The standalone research collector prototype described below can be run independently.

Never commit secrets, API keys, local `.env` files, or user data.

## Standalone research collector prototype

The current Python collector is an early, standalone prototype, not the complete AI agent service. It retrieves public items from Google News RSS, arXiv RSS, the Hacker News Algolia API, and the Hugging Face blog feed. It filters by the configured date window, removes duplicate titles, assigns keyword-based trend tags, and writes timestamped JSON and Markdown reports.

### Run the collector

From the repository root, use Python 3 and install the dependencies:

```bash
python -m venv .venv
# Activate the virtual environment for your shell, then:
python -m pip install -r services/research-worker/agent/requirements.txt
python services/research-worker/agent/collector.py
```

The collector reads its static topic, queries, date window, source toggles, and trend keywords from `services/research-worker/agent/config.json`. Edit that file to change the prototype's configuration. The collector needs network access to retrieve source feeds and API results.

Generated reports are written to `services/research-worker/agent/output/`. They are local run artifacts and are ignored by Git; do not commit generated reports.

### Prototype limitations

This prototype does **not** yet implement the LangGraph agent workflow, configurable LLM invocation, a topic-as-input interface, the shared structured research request/result contract, or API/PostgreSQL integration. Automated tests are still to be added before this prototype PR is ready to merge. These capabilities remain follow-up work; this prototype should not be treated as completion of ORION-5.

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
