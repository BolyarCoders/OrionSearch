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
| Background processing | Worker and scheduler (to be decided) | Long-running and recurring research jobs |
| Infrastructure | Docker, Docker Compose, GitHub Actions | Local environment, CI/CD, and deployment |

The implementation may evolve as the team builds the MVP. AI and search providers should remain configurable, and generated claims should retain links to their supporting sources.

## Development status

The API foundation can run locally; see [the backend README](services/api/README.md) for setup and testing. The rest of the application and local service stack are still being developed. Docker configuration and an example environment file are planned under ORION-14.

Never commit secrets, API keys, local `.env` files, or user data.

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
