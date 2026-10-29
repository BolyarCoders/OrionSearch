"""Regression tests for the current parameterized collector interface."""

import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path

import pytest
from types import SimpleNamespace
import requests

AGENT_DIR = Path(__file__).resolve().parents[1]
if str(AGENT_DIR) not in sys.path:
    sys.path.insert(0, str(AGENT_DIR))

import collector


def test_feed_marks_publication_and_update_dates_separately(monkeypatch):
    stamp = (2026, 10, 20, 12, 30, 0, 0, 0, 0)
    entries = [
        {"title": "One", "link": "https://example.com/1", "published_parsed": stamp},
        {"title": "Two", "link": "https://example.com/2", "updated_parsed": stamp},
    ]
    monkeypatch.setattr(collector.requests, "get", lambda *a, **kw: SimpleNamespace(content=b"feed", raise_for_status=lambda: None))
    monkeypatch.setattr(collector.feedparser, "parse", lambda _: SimpleNamespace(entries=entries))
    result = collector.get_rss("https://example.com/feed", "Example", 2)
    assert [item["publication_date_kind"] for item in result] == ["published", "updated"]


def test_hacker_news_date_is_not_attributed_to_linked_article(monkeypatch):
    hits = [{"objectID": "1", "url": "https://example.com/", "created_at": "2026-10-20T12:00:00Z"},
            {"objectID": "2", "created_at": "2026-10-20T12:00:00Z"}]
    monkeypatch.setattr(collector.requests, "get", lambda *a, **kw: SimpleNamespace(raise_for_status=lambda: None, json=lambda: {"hits": hits}))
    result = collector.search_hacker_news("topic", 7, 2)
    assert [item["publication_date_kind"] for item in result] == ["discovery", "published"]


@pytest.mark.parametrize("changes", [{"title": ""}, {"url": ""}, {"published_at": "not-a-date"}, {"published_at": ""}])
def test_incomplete_articles_are_not_returned(changes):
    item = article()
    item.update(changes)
    assert collector.process_articles([item], days=7) == []


def test_naive_dates_are_utc_and_future_dates_are_rejected():
    naive = (datetime.now(timezone.utc) - timedelta(hours=1)).replace(tzinfo=None).isoformat()
    future = (datetime.now(timezone.utc) + timedelta(days=2)).isoformat()
    result = collector.process_articles([article("valid", published_at=naive), article("future", published_at=future)], days=7)
    assert len(result) == 1
    assert result[0]["published_at"].endswith("+00:00")


def test_feed_propagates_timeout(monkeypatch):
    def fail(*args, **kwargs):
        raise requests.Timeout("timeout")
    monkeypatch.setattr(collector.requests, "get", fail)
    with pytest.raises(requests.Timeout):
        collector.get_rss("https://example.com/feed", "Example", 2)


def article(title="AI agent release", published_at=None, **overrides):
    item = {
        "title": title,
        "url": "https://example.com/article",
        "source": "Example",
        "published_at": published_at
        or (datetime.now(timezone.utc) - timedelta(hours=1)).isoformat(),
        "summary": "New tool use research",
    }
    item.update(overrides)
    return item


@pytest.mark.parametrize("days", [0, -1, 91, True, 1.5])
def test_validate_limits_rejects_invalid_days(days):
    with pytest.raises(ValueError):
        collector.validate_limits(days)


@pytest.mark.parametrize("limit", [0, -1, 51, True, 1.5])
def test_validate_limits_rejects_invalid_article_limits(limit):
    with pytest.raises(ValueError):
        collector.validate_limits(7, limit)


def test_process_articles_normalizes_dates_classifies_and_does_not_mutate():
    original = article("New AI agent with tool use")
    before = original.copy()

    result = collector.process_articles([original], days=7)

    assert len(result) == 1
    assert result[0]["published_at"].endswith("+00:00")
    assert "AI agents" in result[0]["trends"]
    assert original == before


def test_process_articles_deduplicates_and_discards_invalid_or_old_items():
    newer = article("Same AI agent", published_at=datetime.now(timezone.utc).isoformat())
    older = article(
        "Same AI agent!",
        published_at=(datetime.now(timezone.utc) - timedelta(hours=2)).isoformat(),
    )
    old = article(
        "Old article",
        published_at=(datetime.now(timezone.utc) - timedelta(days=10)).isoformat(),
    )
    invalid_date = article("Bad date", published_at="not-a-date")

    result = collector.process_articles([older, newer, old, invalid_date], days=7)

    assert len(result) == 1
    assert result[0]["url"] == newer["url"]


def test_process_articles_applies_optional_limit_after_sorting():
    older = article(
        "Older article",
        published_at=(datetime.now(timezone.utc) - timedelta(hours=3)).isoformat(),
    )
    newer = article(
        "Newer article",
        published_at=(datetime.now(timezone.utc) - timedelta(minutes=5)).isoformat(),
    )

    result = collector.process_articles([older, newer], days=7, max_articles=1)

    assert [item["title"] for item in result] == ["Newer article"]


def test_collect_uses_parameterized_source_calls_and_reports_total(monkeypatch):
    monkeypatch.setattr(collector, "get_feed_limit", lambda: 3)
    monkeypatch.setattr(
        collector, "search_google_news", lambda query, days, limit: [article(query)]
    )

    selected, errors, total = collector.collect(
        topic="AI agents",
        days=7,
        queries=["AI agents"],
        selected_sources=["google_news"],
        max_articles=5,
    )

    assert len(selected) == 1
    assert errors == {}
    assert total == 1
