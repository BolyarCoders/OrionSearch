import os
import sys
import logging
from datetime import datetime, timezone
from pathlib import Path
from time import perf_counter

from dotenv import load_dotenv
from api.models import ResearchRequest, ResearchResult

ROOT = Path(__file__).resolve().parents[1]
AGENT_DIR = ROOT / "agent"

load_dotenv(ROOT / ".env")
load_dotenv(AGENT_DIR / ".env")

if str(AGENT_DIR) not in sys.path:
    sys.path.insert(0, str(AGENT_DIR))

import ai_agent

logger = logging.getLogger("uvicorn.error")


def run_research(request: ResearchRequest) -> ResearchResult:
    if not os.getenv("GEMINI_API_KEY"):
        raise RuntimeError("GEMINI_API_KEY is not configured")

    start = perf_counter()
    client = ai_agent.create_client()

    try:
        logger.info("Starting research: %s", request.topic)

        queries, sources = ai_agent.plan_research(client, request.topic, request.days)

        logger.info("Selected sources: %s", ", ".join(sources))

        articles, errors, total_articles = ai_agent.collect_articles(
            request.topic,
            request.days,
            queries,
            sources,
            max_articles=request.max_articles,
        )

        logger.info(
            "Found %d unique articles, returning %d", total_articles, len(articles)
        )

        report = ai_agent.analyze_articles(
            client, request.topic, request.days, articles
        )

        logger.info("Research completed in %.2fs", perf_counter() - start)

        return ResearchResult(
            topic=request.topic,
            days=request.days,
            queries=queries,
            sources=sources,
            total_articles=total_articles,
            articles=articles,
            report=report,
            errors=errors,
            generated_at=datetime.now(timezone.utc),
        )

    finally:
        client.close()
