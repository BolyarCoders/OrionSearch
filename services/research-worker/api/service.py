import os
import sys
from datetime import datetime, timezone
from pathlib import Path
from threading import Lock

from dotenv import load_dotenv

from api.models import ResearchRequest, ResearchResult

ROOT = Path(__file__).resolve().parents[1]
AGENT_DIR = ROOT / "agent"
load_dotenv(ROOT / ".env")
load_dotenv(AGENT_DIR / ".env")

# The existing ai_agent.py imports collector.py as a top-level module.
if str(AGENT_DIR) not in sys.path:
    sys.path.insert(0, str(AGENT_DIR))

# collector.CONFIG is global and gets modified during collection.
# Keep one research run at a time until it is refactored to accept per-run config.
research_lock = Lock()


def run_research(request: ResearchRequest) -> ResearchResult:
    if not os.getenv("GEMINI_API_KEY"):
        raise RuntimeError("GEMINI_API_KEY is not configured")

    import ai_agent
    from google import genai

    with research_lock:
        client = genai.Client(api_key=os.environ["GEMINI_API_KEY"])
        try:
            queries, sources = ai_agent.plan_research(client, request.topic, request.days)
            articles, errors = ai_agent.collect_articles(request.topic, request.days, queries, sources)
            report = ai_agent.analyze_articles(client, request.topic, request.days, articles)
            ai_agent.save_report(request.topic, request.days, queries, articles, errors, report)
        finally:
            client.close()

    return ResearchResult(
        topic=request.topic,
        days=request.days,
        queries=queries,
        sources=sources,
        total_articles=len(articles),
        articles=articles[:request.max_articles],
        report=report,
        errors=errors,
        generated_at=datetime.now(timezone.utc),
    )
