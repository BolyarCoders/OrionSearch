import os
import sys
import logging
from datetime import datetime, timezone
from pathlib import Path
from threading import Lock
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
research_lock = Lock()


class ResearchBusy(RuntimeError):
    pass


def run_contract_research(request):
    from api.discovery import build_result, execution_window

    days, _, _, _ = execution_window(request)
    if not os.getenv("GEMINI_API_KEY"):
        logger.warning("Research provider is not configured")
        raise RuntimeError("Research provider is not configured")
    if not research_lock.acquire(blocking=False):
        raise ResearchBusy("Research capacity is in use")
    uploader = None
    try:
        if os.getenv("ORION_CAPTURE_ENABLED") == "true":
            from api.captures import CaptureUploader
            uploader = CaptureUploader()
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
            selected = [name for name in selected if name in collector.SOURCES]
            if not selected:
                logger.warning("Research has no enabled discovery sources")
                raise RuntimeError("No discovery source is enabled")
            articles, errors, _ = ai_agent.collect_articles(topic, days, queries, selected, max_articles=50)
            if errors:
                logger.warning("Research source collection did not complete")
                raise RuntimeError("Source collection did not complete")
            selected_articles = []
            result = build_result(request, queries, selected, articles, selected_articles=selected_articles)
            if request.get("includeReport", False):
                # Use exactly the filtered, deduplicated and ordered sources for
                # the report so its numbered citations refer to accepted sources.
                report_articles = []
                for source, article in zip(result["sources"], selected_articles, strict=True):
                    report_articles.append({
                        "title": source.get("title") or "Untitled",
                        "url": source["canonicalUrl"],
                        "source": str(article.get("source") or "Unknown")[:200],
                        "published_at": source.get("publishedAt") or "Unknown",
                        "summary": str(article.get("summary") or "")[:350],
                    })
                try:
                    result["report"] = ai_agent.analyze_articles(client, topic, days, report_articles)
                except Exception:
                    logger.warning("Research report generation failed")
                    raise
            if uploader is not None:
                from api.captures import enrich_captures
                result = enrich_captures(result, request, uploader)
            return result
        finally:
            client.close()
    finally:
        try:
            if uploader is not None:
                uploader.close()
        finally:
            research_lock.release()


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
