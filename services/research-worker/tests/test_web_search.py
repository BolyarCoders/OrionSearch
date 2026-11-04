"""Topic-agnostic discovery regression tests (no external network calls)."""
import json
import sys
from datetime import datetime, timezone
from pathlib import Path

import pytest

AGENT = Path(__file__).resolve().parents[1] / "agent"
if str(AGENT) not in sys.path:
    sys.path.insert(0, str(AGENT))

import collector
import web_search
from api.contract import CONTRACT_ROOT
from api.discovery import build_result


@pytest.mark.parametrize("topic,host", [
    ("fashion trends 2026", "fashion.example"),
    ("renewable energy in farming", "agriculture.example"),
    ("new electric vehicles", "cars.example"),
    ("Българска литература", "books.example"),
])
def test_web_search_accepts_any_topic_without_curated_domains(monkeypatch, topic, host):
    calls = []

    class FakeDDGS:
        def __init__(self, timeout):
            assert timeout == 10

        def text(self, query, max_results):
            calls.append((query, max_results))
            return [
                {"href": f"https://{host}/relevant", "title": topic,
                 "body": "Topic-specific results"},
                {"href": "javascript:alert(1)", "title": "Invalid"},
            ]

    monkeypatch.delenv("BRAVE_SEARCH_API_KEY", raising=False)
    monkeypatch.setattr(web_search, "DDGS", FakeDDGS)
    articles = web_search.search_web(topic, 90, 8)

    assert calls == [(topic, 8)]
    assert len(articles) == 1
    assert articles[0]["url"] == f"https://{host}/relevant"
    assert articles[0]["published_at"] is None
    assert articles[0]["publication_date_kind"] == "discovery"
    assert articles[0]["search_rank"] == 0


def test_brave_uses_configured_key_and_official_endpoint(monkeypatch):
    def fake_get(url, headers, params, timeout):
        assert url == web_search.BRAVE_URL
        assert headers["X-Subscription-Token"] == "test-key"
        assert params["q"] == "street fashion"
        assert params["count"] == 3
        assert timeout == (3, 10)

        class Response:
            def raise_for_status(self):
                return None

            def json(self):
                return {"web": {"results": [
                    {"url": "https://fashion.example/a", "title": "Street fashion", "description": "Fashion report"}
                ]}}
        return Response()

    monkeypatch.setenv("BRAVE_SEARCH_API_KEY", "test-key")
    monkeypatch.setattr(web_search.requests, "get", fake_get)
    monkeypatch.setattr(web_search, "DDGS", lambda **kwargs: pytest.fail("unnecessary fallback"))
    result = web_search.search_web("street fashion", 7, 3)
    assert result[0]["source"] == "Web Search"


def test_only_explicit_web_discoveries_without_dates_survive_processing():
    now = datetime.now(timezone.utc).isoformat()
    articles = [
        {"title": "Relevant clothing styles", "url": "https://fashion.example/1",
         "source": "Web Search", "published_at": None,
         "publication_date_kind": "discovery", "search_rank": 0},
        {"title": "Updated RSS item", "url": "https://rss.example/1",
         "source": "Google News", "published_at": None},
        {"title": "Current news", "url": "https://news.example/1",
         "source": "Google News", "published_at": now},
    ]
    results = collector.process_articles(articles, days=7)
    assert [article["title"] for article in results] == [
        "Current news", "Relevant clothing styles"
    ]
    assert results[1]["published_at"] is None


def test_collector_prioritizes_web_relevance_without_hacker_news(monkeypatch):
    import web_search as adapter
    import news_search as news_adapter

    monkeypatch.delenv("RESEARCH_SOURCES", raising=False)
    monkeypatch.setattr(collector, "get_feed_limit", lambda: 5)

    def mock_web(query, days, limit):
        return [
            {"title": f"{query} primary", "url": "https://fashion.example/one",
             "source": "Web Search", "published_at": None,
             "publication_date_kind": "discovery", "search_rank": 0},
            {"title": f"{query} secondary", "url": "https://fashion.example/two",
             "source": "Web Search", "published_at": None,
             "publication_date_kind": "discovery", "search_rank": 1},
        ]

    def mock_news(query, days, limit):
        return [{"title": "Recent news", "url": "https://news.example/recent",
                 "source": "Google News", "published_at": datetime.now(timezone.utc).isoformat()}]

    monkeypatch.setattr(news_adapter, "search_news", lambda query, days, limit: [])
    monkeypatch.setattr(adapter, "search_web", mock_web)
    monkeypatch.setattr(collector, "search_google_news", mock_news)
    assert collector.get_selected_sources() == ["news_search", "google_news", "web_search"]

    selected, errors, total = collector.collect(
        "fashion trends", 7, ["fashion trends"],
        collector.get_selected_sources(), max_articles=2
    )
    assert errors == {}
    assert total == 3
    assert [a["url"] for a in selected] == [
        "https://news.example/recent", "https://fashion.example/one"
    ]


def test_contract_retains_web_search_query_and_source_ranking():
    request = json.loads((CONTRACT_ROOT / "v1/request.json").read_text(encoding="utf-8"))
    request["settings"]["config"] = {"maxArticles": 3}
    articles = [
        {"title": "First", "url": "https://first.example/a", "source": "Web Search", "search_rank": 0},
        {"title": "Second", "url": "https://second.example/b", "source": "Web Search", "search_rank": 1},
        {"title": "Third", "url": "https://third.example/c", "source": "Google News"},
    ]
    result = build_result(request, ["fashion trends"], ["web_search"], articles)
    assert result["queries"] == [{"provider": "web_search", "query": "fashion trends"}]
    assert [source["title"] for source in result["sources"]] == ["First", "Second", "Third"]

    request["settings"]["preferredDomains"] = ["second.example"]
    result = build_result(request, ["fashion trends"], ["web_search"], articles)
    assert [source["title"] for source in result["sources"]] == ["Second", "First", "Third"]
