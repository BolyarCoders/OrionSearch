import copy
import json
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path
from types import SimpleNamespace
from urllib.parse import parse_qs, urlparse

import pytest
import requests

AGENT_DIR = Path(__file__).resolve().parents[1]
if str(AGENT_DIR) not in sys.path:
    sys.path.insert(0, str(AGENT_DIR))

import collector


@pytest.fixture
def config(monkeypatch):
    original = copy.deepcopy(collector.CONFIG)
    configured = copy.deepcopy(original)
    configured["days_back"] = 7
    configured["max_per_source"] = 2
    configured["top_results"] = 1
    configured["topic"] = "Test topic"
    configured["queries"] = ["AI agents", "research"]
    configured["sources"] = {
        "google_news": True,
        "arxiv": True,
        "hacker_news": True,
        "hugging_face": True,
    }
    configured["trend_categories"] = {
        "AI agents": ["agent", "tool use"],
        "Robotics": ["robot"],
    }
    collector.CONFIG.clear()
    collector.CONFIG.update(configured)
    yield collector.CONFIG
    collector.CONFIG.clear()
    collector.CONFIG.update(original)


def article(title="AI agent release", published_at=None, **overrides):
    item = {
        "title": title,
        "url": "https://example.com/article",
        "source": "Example",
        "published_at": published_at
        if published_at is not None\n        else (datetime.now(timezone.utc) - timedelta(days=1)).isoformat(),
        "summary": "A new tool",
    }
    item.update(overrides)
    return item


def test_process_articles_filters_normalizes_deduplicates_and_sorts(config):
    older = article("Older robot", published_at=(datetime.now(timezone.utc) - timedelta(days=3)).isoformat())
    newer = article("AI: agent models!", published_at=(datetime.now(timezone.utc) - timedelta(hours=1)).isoformat())
    duplicate = article("AI agent models", published_at=datetime.now(timezone.utc).isoformat())
    result = collector.process_articles([older, newer, duplicate])

    assert [item["title"] for item in result] == ["AI: agent models!", "Older robot"]
    assert result[0]["published_at"].endswith("+00:00")
    assert result[0]["trends"] == ["AI agents"]
    # The first occurrence of a normalized title wins.
    assert result[0]["url"] == newer["url"]


@pytest.mark.parametrize(
    "overrides",
    [
        {"title": ""},
        {"url": ""},
        {"published_at": ""},
        {"published_at": "not-a-date"},
    ],
)
def test_process_articles_skips_incomplete_or_invalid_items(config, overrides):
    assert collector.process_articles([article(**overrides)]) == []


def test_process_articles_rejects_old_and_far_future_dates(config):
    old = article(published_at=(datetime.now(timezone.utc) - timedelta(days=9)).isoformat())
    future = article(published_at=(datetime.now(timezone.utc) + timedelta(days=2)).isoformat())
    assert collector.process_articles([old, future]) == []


def test_process_articles_accepts_naive_timestamp_as_utc(config):
    naive = (datetime.now(timezone.utc) - timedelta(hours=2)).replace(tzinfo=None).isoformat()
    result = collector.process_articles([article(published_at=naive)])
    assert len(result) == 1
    assert result[0]["published_at"].endswith("+00:00")


def test_get_rss_maps_feed_entries_and_respects_limit(monkeypatch, config):
    parsed_date = (2026, 10, 20, 12, 30, 0, 0, 0, 0)
    entries = [
        {
            "title": "<b>First article</b>",
            "link": "https://example.com/1",
            "published_parsed": parsed_date,
            "summary": "<p>Summary</p>",
        },
        {"title": "Second", "link": "https://example.com/2", "updated_parsed": parsed_date},
        {"title": "Ignored third", "link": "https://example.com/3"},
    ]
    response = SimpleNamespace(content=b"<feed/>", raise_for_status=lambda: None)
    get = SimpleNamespace()
    calls = []

    def fake_get(url, **kwargs):
        calls.append((url, kwargs))
        return response

    monkeypatch.setattr(collector.requests, "get", fake_get)
    monkeypatch.setattr(collector.feedparser, "parse", lambda content: SimpleNamespace(entries=entries))

    results = collector.get_rss("https://example.com/feed", "Example")

    assert len(results) == 2
    assert results[0] == {
        "title": "First article",
        "url": "https://example.com/1",
        "source": "Example",
        "published_at": "2026-10-20T12:30:00+00:00",
        "summary": "Summary",
    }
    assert results[1]["published_at"] == "2026-10-20T12:30:00+00:00"
    assert calls[0][1]["timeout"] == 20
    assert calls[0][1]["headers"]["User-Agent"] == "OrionSearch/0.1"


def test_get_rss_propagates_http_errors(monkeypatch):
    def fail(*args, **kwargs):
        raise requests.Timeout("timeout")

    monkeypatch.setattr(collector.requests, "get", fail)
    with pytest.raises(requests.Timeout):
        collector.get_rss("https://example.com/feed", "Example")


def test_google_news_builds_one_rss_request_per_query(monkeypatch, config):
    calls = []
    monkeypatch.setattr(
        collector,
        "get_rss",
        lambda url, source: calls.append((url, source)) or [],
    )

    collector.search_google_news()

    assert len(calls) == 2
    assert all(source == "Google News" for _, source in calls)
    assert parse_qs(urlparse(calls[0][0]).query)["q"] == ["AI agents when:7d"]
    assert parse_qs(urlparse(calls[0][0]).query)["hl"] == ["en-US"]


@pytest.mark.parametrize(
    ("function_name", "expected_url", "expected_source"),
    [
        ("search_arxiv", "https://rss.arxiv.org/rss/cs.AI+cs.LG+cs.CL", "arXiv"),
        ("search_hugging_face", "https://huggingface.co/blog/feed.xml", "Hugging Face"),
    ],
)
def test_rss_source_adapters_use_expected_feed(monkeypatch, function_name, expected_url, expected_source):
    calls = []
    monkeypatch.setattr(
        collector,
        "get_rss",
        lambda url, source: calls.append((url, source)) or [],
    )

    getattr(collector, function_name)()

    assert calls == [(expected_url, expected_source)]


def test_hacker_news_maps_hits_and_falls_back_to_item_url(monkeypatch, config):
    responses = [
        {"hits": [{"title": "Story", "objectID": "123", "created_at": "2026-10-20T12:00:00Z"}]},
        {"hits": []},
    ]
    calls = []

    class FakeResponse:
        def __init__(self, payload):
            self.payload = payload

        def raise_for_status(self):
            pass

        def json(self):
            return self.payload

    def fake_get(url, **kwargs):
        calls.append((url, kwargs))
        return FakeResponse(responses[len(calls) - 1])

    monkeypatch.setattr(collector.requests, "get", fake_get)
    results = collector.search_hacker_news()

    assert len(calls) == 2
    assert calls[0][1]["params"]["query"] == "AI agents"
    assert calls[0][1]["params"]["tags"] == "story"
    assert calls[0][1]["params"]["hitsPerPage"] == 2
    assert results[0]["url"] == "https://news.ycombinator.com/item?id=123"
    assert results[0]["source"] == "Hacker News"


def test_save_results_writes_json_and_markdown(tmp_path, monkeypatch, config):
    output = tmp_path / "generated"
    monkeypatch.setattr(collector, "OUTPUT_DIR", output)
    items = [
        article("AI agent one", trends=["AI agents"]),
        article("Robot two", trends=["Robotics"]),
    ]

    collector.save_results(items, {"hacker_news": "temporary failure"})

    json_files = list(output.glob("*.json"))
    markdown_files = list(output.glob("*.md"))
    assert len(json_files) == len(markdown_files) == 1
    payload = json.loads(json_files[0].read_text(encoding="utf-8"))
    assert payload["topic"] == "Test topic"
    assert payload["total_results"] == 2
    assert len(payload["articles"]) == 1
    assert payload["trends"] == {"AI agents": 1, "Robotics": 1}
    assert payload["errors"] == {"hacker_news": "temporary failure"}
    report = markdown_files[0].read_text(encoding="utf-8")
    assert "# OrionSearch: Test topic" in report
    assert "temporary failure" in report


def test_main_isolates_source_failure_and_saves_remaining_results(monkeypatch, config):
    config["sources"] = {
        "google_news": True,
        "arxiv": True,
        "hacker_news": False,
        "hugging_face": False,
    }
    monkeypatch.setattr(
        collector, "search_google_news", lambda: [article("Working article", trends=[])]
    )

    def failed_source():
        raise requests.RequestException("source unavailable")

    monkeypatch.setattr(collector, "search_arxiv", failed_source)
    saved = {}
    monkeypatch.setattr(collector, "process_articles", lambda items: items)
    monkeypatch.setattr(
        collector,
        "save_results",
        lambda items, errors: saved.update(articles=items, errors=errors),
    )

    collector.main()

    assert len(saved["articles"]) == 1
    assert saved["errors"] == {"arxiv": "source unavailable"}
