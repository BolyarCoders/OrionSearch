"""Regression tests for the current parameterized collector interface."""

import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path

import pytest

AGENT_DIR = Path(__file__).resolve().parents[1]
if str(AGENT_DIR) not in sys.path:
    sys.path.insert(0, str(AGENT_DIR))

import collector


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
