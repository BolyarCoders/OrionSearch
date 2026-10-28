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


class ResearchBusy(RuntimeError):
    pass


def run_contract_research(request):
    from api.discovery import build_result, execution_window

    days, _, _, _ = execution_window(request)
    if not os.getenv("GEMINI_API_KEY"):
        raise RuntimeError("Research provider is not configured")
    if not research_lock.acquire(blocking=False):
        raise ResearchBusy("Research capacity is in use")
    try:
        import ai_agent
        import collector
        from google import genai
        from google.genai import types

        # Preferences are research data. Only these fixed source adapters can be selected.
        topic = request["title"] + "\nDescription: " + (request["description"] or "") + "\nResearch objective: " + request["objective"]
        client = genai.Client(api_key=os.environ["GEMINI_API_KEY"], http_options=types.HttpOptions(
            timeout=60000, retry_options=types.HttpRetryOptions(attempts=1)))
        try:
            queries, selected = ai_agent.plan_research(client, topic, days)
            if not queries or len(queries) > 5 or any(not isinstance(query, str) or not query.strip() or len(query) > 1000 for query in queries):
                from api.contract import ContractError
                raise ContractError("Invalid research plan")
            selected = [name for name in selected if name in ai_agent.SOURCES and collector.CONFIG["sources"].get(name, False)]
            if not selected:
                raise RuntimeError("No discovery source is enabled")
            articles, errors = ai_agent.collect_articles(topic, days, queries, selected, persist_output=False)
            if errors:
                raise RuntimeError("Source collection did not complete")
            return build_result(request, queries, selected, articles)
        finally:
            client.close()
    finally:
        research_lock.release()


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
