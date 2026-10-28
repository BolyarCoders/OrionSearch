import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path
from threading import Barrier
from types import SimpleNamespace

import pytest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "agent"))

import ai_agent
import collector
from api import service
from api.models import ResearchRequest


def article(title, hours_ago=1):
    return {
        "title": title,
        "url": f"https://example.com/{title}",
        "source": "Test",
        "published_at": (
            datetime.now(timezone.utc) - timedelta(hours=hours_ago)
        ).isoformat(),
        "summary": "Test summary",
    }


@pytest.mark.parametrize("topic", ["  ", "\\t\\n", "   "])
def test_rejects_blank_research_topic(topic):
    with pytest.raises(ValueError, match="Research topic cannot be blank"):
        ResearchRequest(topic=topic)


def test_research_topic_is_trimmed():
    request = ResearchRequest(topic="  Artificial Intelligence  ")
    assert request.topic == "Artificial Intelligence"


def test_date_filter_and_deduplication():
    items = [
        article("Same!", 4),
        article("Same", 1),
        article("Recent", 0.5),
        article("Expired", 48),
        article("Future", -4),
        {**article("Invalid"), "published_at": "invalid"},
    ]

    results = collector.process_articles(items, 1)

    assert len(results) == 2
    assert results[0]["title"] == "Recent"
    assert results[1]["title"] == "Same"
    assert all("trends" in item for item in results)


def test_max_articles():
    items = [article(f"Article {i}", i + 1) for i in range(5)]

    results = collector.process_articles(items, 1, max_articles=2)

    assert len(results) == 2
    assert results[0]["title"] == "Article 0"


@pytest.mark.parametrize("days", [0, -1, 91])
def test_invalid_days(days):
    with pytest.raises(ValueError):
        collector.validate_limits(days, 10)


@pytest.mark.parametrize("limit", [0, -1, 51])
def test_invalid_max_articles(limit):
    with pytest.raises(ValueError):
        collector.validate_limits(7, limit)


@pytest.mark.parametrize("raw", ["abc", "0", "-1", "101", "2.5"])
def test_invalid_feed_limit(monkeypatch, raw):
    monkeypatch.setenv("RESEARCH_FEED_LIMIT", raw)

    with pytest.raises(ValueError, match="RESEARCH_FEED_LIMIT"):
        collector.get_feed_limit()


def test_default_feed_limit(monkeypatch):
    monkeypatch.delenv("RESEARCH_FEED_LIMIT", raising=False)
    assert collector.get_feed_limit() == 12


def test_valid_feed_limit(monkeypatch):
    monkeypatch.setenv("RESEARCH_FEED_LIMIT", "8")
    assert collector.get_feed_limit() == 8


def test_default_sources(monkeypatch):
    monkeypatch.delenv("RESEARCH_SOURCES", raising=False)

    queries, sources = ai_agent.plan_research(None, "Artificial Intelligence", 7)

    assert queries == ["Artificial Intelligence"]
    assert sources == ["google_news", "hacker_news"]


def test_explicit_sources(monkeypatch):
    monkeypatch.setenv("RESEARCH_SOURCES", "arxiv,hugging_face")

    queries, sources = ai_agent.plan_research(None, "AI", 7)

    assert queries == ["AI"]
    assert sources == ["arxiv", "hugging_face"]


@pytest.mark.parametrize(
    "value", ["", "invalid_source", "google_news,invalid_source", "google_news,"]
)
def test_invalid_sources(monkeypatch, value):
    monkeypatch.setenv("RESEARCH_SOURCES", value)

    with pytest.raises(ValueError):
        ai_agent.plan_research(None, "AI", 7)


def test_all_source_task_signatures(monkeypatch):
    monkeypatch.setenv("RESEARCH_FEED_LIMIT", "8")
    calls = []

    def google(query, days, limit):
        calls.append(("google_news", query, days, limit))
        return []

    def hacker(query, days, limit):
        calls.append(("hacker_news", query, days, limit))
        return []

    def arxiv(limit):
        calls.append(("arxiv", limit))
        return []

    def hugging(limit):
        calls.append(("hugging_face", limit))
        return []

    monkeypatch.setattr(collector, "search_google_news", google)
    monkeypatch.setattr(collector, "search_hacker_news", hacker)
    monkeypatch.setattr(collector, "search_arxiv", arxiv)
    monkeypatch.setattr(collector, "search_hugging_face", hugging)

    articles, errors, total = collector.collect(
        "AI", 3, ["query-a", "query-b"], collector.SOURCES, 5
    )

    assert articles == []
    assert errors == {}
    assert total == 0
    assert set(calls) == {
        ("google_news", "query-a", 3, 8),
        ("google_news", "query-b", 3, 8),
        ("hacker_news", "query-a", 3, 8),
        ("hacker_news", "query-b", 3, 8),
        ("arxiv", 8),
        ("hugging_face", 8),
    }


def test_sources_execute_concurrently(monkeypatch):
    monkeypatch.setenv("RESEARCH_FEED_LIMIT", "8")
    barrier = Barrier(2)

    def search(*args):
        barrier.wait(timeout=5)
        return []

    monkeypatch.setattr(collector, "search_google_news", search)
    monkeypatch.setattr(collector, "search_hacker_news", search)

    articles, errors, total = collector.collect(
        "AI", 1, ["AI"], ["google_news", "hacker_news"], 5
    )

    assert articles == []
    assert errors == {}
    assert total == 0


def test_multiple_failures_same_source(monkeypatch):
    monkeypatch.setenv("RESEARCH_FEED_LIMIT", "8")

    def failing_google(query, days, limit):
        raise RuntimeError(f"Failed query: {query}")

    def successful_hacker(query, days, limit):
        return [article("Successful article")]

    monkeypatch.setattr(collector, "search_google_news", failing_google)
    monkeypatch.setattr(collector, "search_hacker_news", successful_hacker)

    articles, errors, total = collector.collect(
        "AI", 1, ["first", "second"], ["google_news", "hacker_news"], 5
    )

    assert total == 1
    assert len(articles) == 1
    assert "google_news:first" in errors
    assert "google_news:second" in errors
    assert errors["google_news:first"] != errors["google_news:second"]


def test_all_sources_failure(monkeypatch):
    monkeypatch.setenv("RESEARCH_FEED_LIMIT", "8")

    def failing_search(*args):
        raise RuntimeError("Source unavailable")

    monkeypatch.setattr(collector, "search_google_news", failing_search)
    monkeypatch.setattr(collector, "search_hacker_news", failing_search)

    articles, errors, total = collector.collect(
        "AI", 1, ["AI"], ["google_news", "hacker_news"], 5
    )

    assert articles == []
    assert total == 0
    assert len(errors) == 2
    assert "google_news:AI" in errors
    assert "hacker_news:AI" in errors


@pytest.mark.parametrize("status", [503, 504])
def test_gemini_fallback(monkeypatch, status):
    calls = []

    class FakeServerError(Exception):
        def __init__(self, code):
            super().__init__(f"Gemini error {code}")
            self.code = code

    class FakeModels:
        def generate_content(self, *, model, contents, config):
            calls.append(model)

            if model == "primary":
                raise FakeServerError(status)

            return SimpleNamespace(text="Analysis completed")

    monkeypatch.setattr(ai_agent.errors, "ServerError", FakeServerError)
    monkeypatch.setattr(ai_agent, "MODEL", "primary")
    monkeypatch.setenv("GEMINI_FALLBACK_MODEL", "fallback")

    client = SimpleNamespace(models=FakeModels())
    result = ai_agent.ask_ai(client, "Analyze this")

    assert result == "Analysis completed"
    assert calls == ["primary", "fallback"]


def test_gemini_nonretryable_error(monkeypatch):
    calls = []

    class FakeServerError(Exception):
        def __init__(self, code):
            super().__init__(f"Gemini error {code}")
            self.code = code

    class FakeModels:
        def generate_content(self, *, model, contents, config):
            calls.append(model)
            raise FakeServerError(500)

    monkeypatch.setattr(ai_agent.errors, "ServerError", FakeServerError)
    monkeypatch.setattr(ai_agent, "MODEL", "primary")
    monkeypatch.setenv("GEMINI_FALLBACK_MODEL", "fallback")

    with pytest.raises(FakeServerError):
        ai_agent.ask_ai(SimpleNamespace(models=FakeModels()), "Analyze")

    assert calls == ["primary"]


def test_api_total_articles_semantics(monkeypatch):
    monkeypatch.setenv("GEMINI_API_KEY", "test-key")

    class FakeClient:
        closed = False

        def close(self):
            self.closed = True

    client = FakeClient()

    monkeypatch.setattr(service.ai_agent, "create_client", lambda: client)
    monkeypatch.setattr(
        service.ai_agent, "plan_research", lambda *args: (["AI"], ["google_news"])
    )
    monkeypatch.setattr(
        service.ai_agent,
        "collect_articles",
        lambda *args, **kwargs: ([article("Selected")], {}, 17),
    )
    monkeypatch.setattr(
        service.ai_agent, "analyze_articles", lambda *args: "Research report"
    )

    request = ResearchRequest(topic="Artificial Intelligence", days=1, max_articles=1)

    result = service.run_research(request)

    assert result.total_articles == 17
    assert len(result.articles) == 1
    assert result.report == "Research report"
    assert result.errors == {}
    assert client.closed
