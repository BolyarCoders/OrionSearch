"""Regressions for time-bounded event-focused research."""
import json
import sys
from datetime import date, datetime, timedelta, timezone
from pathlib import Path

import pytest

AGENT = Path(__file__).resolve().parents[1] / "agent"
if str(AGENT) not in sys.path:
    sys.path.insert(0, str(AGENT))

import news_search
import collector
import ai_agent
from api.contract import CONTRACT_ROOT, ContractError
from api.discovery import execution_window, published_within_window


def fixture_request():
    return json.loads((CONTRACT_ROOT / "v1/request.json").read_text(encoding="utf-8"))


def test_user_dates_take_priority_over_90_day_default():
    req = fixture_request()
    req["settings"].update(dateFrom="2026-03-01", dateTo="2026-03-31")
    days, start, end, maximum = execution_window(req, today=date(2026, 11, 4))
    assert (start, end, days) == (date(2026, 3, 1), date(2026, 3, 31), 249)
    assert maximum == 20


def test_default_date_window_is_last_90_days():
    days, start, end, maximum = execution_window(fixture_request(), today=date(2026, 11, 4))
    assert days == 90
    assert (start, end) == (date(2026, 8, 7), date(2026, 11, 4))


@pytest.mark.parametrize("start,end", [
    ("2027-01-01", "2027-02-01"),
    ("2026-11-05", None),
    ("2010-01-01", "2010-02-01"),
])
def test_future_or_unreasonably_old_windows_rejected(start, end):
    req = fixture_request()
    req["settings"].update(dateFrom=start, dateTo=end)
    with pytest.raises(ContractError):
        execution_window(req, today=date(2026, 11, 4))


def test_unknown_dates_are_never_treated_as_in_period():
    start, end = date(2026, 10, 1), date(2026, 10, 31)
    assert not published_within_window({}, start, end)
    assert not published_within_window({"publishedAt": None}, start, end)
    assert not published_within_window({"publishedAt": "2026-09-30T23:59:00Z"}, start, end)
    assert published_within_window({"publishedAt": "2026-10-01T00:00:00Z"}, start, end)
    assert published_within_window({"publishedAt": "2026-10-31T23:59:00Z"}, start, end)
    assert not published_within_window({"publishedAt": "2026-11-01T00:00:00Z"}, start, end)


def test_news_search_discovers_dated_articles_for_arbitrary_topic(monkeypatch):
    calls = []
    class Fake:
        def __init__(self, timeout):
            assert timeout == 12

        def news(self, query, max_results):
            calls.append((query, max_results))
            return [
                {"title": "Tesla announces new electric vehicle", "url": "https://cars.example/new",
                 "date": "2026-11-02T12:30:00+00:00", "body": "New EV model launch"},
                {"title": "Civic Information API", "url": "https://developers.example/doc"},
                {"title": "Invalid", "url": "javascript:alert(1)", "date": "2026-11-02T12:00:00Z"},
            ]
    monkeypatch.setattr(news_search, "DDGS", Fake)
    items = news_search.search_news("electric vehicles", 7, 12)
    assert calls == [("electric vehicles", 12)]
    assert len(items) == 1
    assert items[0]["publication_date_kind"] == "published"
    assert items[0]["published_at"] == "2026-11-02T12:30:00+00:00"
    assert items[0]["source"] == "News Search"


def test_event_query_expansion_keeps_topic_and_adds_specific_queries(monkeypatch):
    monkeypatch.setattr(ai_agent, "ask_ai", lambda client, prompt:
        '{"queries":["electric car model launches 2026", "Tesla new EV announcements", "BYD battery electric vehicle news"]}')
    queries = ai_agent.expand_news_queries(object(), "electric vehicles", ["electric vehicles"])
    assert queries == [
        "electric vehicles", "electric car model launches 2026",
        "Tesla new EV announcements", "BYD battery electric vehicle news"
    ]


def test_new_news_provider_is_enabled_by_default(monkeypatch):
    monkeypatch.delenv("RESEARCH_SOURCES", raising=False)
    assert collector.get_selected_sources() == ["news_search", "google_news", "web_search"]


def test_collector_keeps_dated_news_below_maximum(monkeypatch):
    import web_search
    monkeypatch.setattr(collector, "get_feed_limit", lambda: 10)
    def fake_news(query, days, limit):
        now = datetime.now(timezone.utc)
        return [
            {"url": f"https://ev.example/{i}", "title": f"New EV model {i}",
             "source": "News Search", "publication_date_kind": "published",
             "published_at": (now - timedelta(days=i)).isoformat()}
            for i in range(1, 9)
        ]
    monkeypatch.setattr(news_search, "search_news", fake_news)
    monkeypatch.setattr(collector, "search_google_news", lambda *args: [])
    monkeypatch.setattr(web_search, "search_web", lambda *args: [])
    results, errors, total = collector.collect(
        "electric vehicles", 14, ["electric vehicles"],
        ["news_search", "google_news", "web_search"], max_articles=8
    )
    assert errors == {}
    assert total == 8
    assert len(results) == 8
    assert all(article["source"] == "News Search" for article in results)


def test_integrated_pipeline_saves_only_requested_publication_dates(monkeypatch):
    from google import genai
    from api import service, source_metadata
    from api.contract import validate_result

    req = fixture_request()
    req["objective"] = "electric vehicles"
    req["title"] = "EV news"
    req["settings"].update(
        dateFrom="2026-10-01", dateTo="2026-10-15",
        preferredDomains=[], excludedDomains=[], config={"maxArticles": 10},
    )
    monkeypatch.setenv("GEMINI_API_KEY", "fake")
    monkeypatch.delenv("ORION_CAPTURE_ENABLED", raising=False)
    monkeypatch.delenv("RESEARCH_ENGLISH_ONLY", raising=False)
    monkeypatch.setattr(genai, "Client", lambda **kwargs: type("Client", (), {"close": lambda self: None})())
    monkeypatch.setattr(service.ai_agent, "translate_search_query", lambda client, objective: objective)
    monkeypatch.setattr(service.ai_agent, "plan_research", lambda *args: (["electric vehicles"], ["news_search"]))
    monkeypatch.setattr(service.ai_agent, "expand_news_queries", lambda client, objective, queries: queries)
    def collected(*args, **kwargs):
        return ([
            {"url": "https://cars.example/old", "title": "Old car", "published_at": "2026-09-30T12:00:00Z", "publication_date_kind": "published"},
            {"url": "https://cars.example/new", "title": "EV model launch", "published_at": "2026-10-11T12:00:00Z", "publication_date_kind": "published", "summary": "New electric car"},
            {"url": "https://cars.example/future", "title": "Future car", "published_at": "2026-10-16T12:00:00Z", "publication_date_kind": "published"},
            {"url": "https://cars.example/wiki", "title": "Generic electric vehicle definition", "published_at": None, "publication_date_kind": "discovery"},
        ], {}, 4)
    monkeypatch.setattr(service.ai_agent, "collect_articles", collected)
    monkeypatch.setattr(service.ai_agent, "select_relevant_articles", lambda client, objective, queries, articles: articles)
    monkeypatch.setattr(source_metadata, "enrich_sources", lambda sources, excluded: None)
    result = service.run_contract_research(req)
    assert [source["canonicalUrl"] for source in result["sources"]] == ["https://cars.example/new"]
    assert result["sources"][0]["publishedAt"] == "2026-10-11T12:00:00Z"
    assert validate_result(result, req)


def test_undated_news_only_qualifies_after_verified_html_publication(monkeypatch):
    from google import genai
    from api import service, source_metadata

    req = fixture_request()
    req["objective"] = "electric vehicles"
    req["settings"].update(dateFrom="2026-10-01", dateTo="2026-10-15")
    monkeypatch.setenv("GEMINI_API_KEY", "fake")
    monkeypatch.delenv("ORION_CAPTURE_ENABLED", raising=False)
    monkeypatch.delenv("RESEARCH_ENGLISH_ONLY", raising=False)
    monkeypatch.setattr(genai, "Client", lambda **kwargs: type("Client", (), {"close": lambda self: None})())
    monkeypatch.setattr(service.ai_agent, "translate_search_query", lambda client, objective: objective)
    monkeypatch.setattr(service.ai_agent, "plan_research", lambda *args: (["electric vehicles"], ["news_search"]))
    monkeypatch.setattr(service.ai_agent, "expand_news_queries", lambda client, objective, queries: queries)
    articles = [{"url": "https://carnews.example/new", "title": "New EV car",
                 "published_at": None, "publication_date_kind": "discovery"}]
    monkeypatch.setattr(service.ai_agent, "collect_articles", lambda *args, **kwargs: (articles, {}, 1))
    monkeypatch.setattr(service.ai_agent, "select_relevant_articles", lambda client, objective, queries, articles: articles)
    def enrich(sources, excluded):
        sources[0]["publishedAt"] = "2026-10-14T08:00:00Z"
    monkeypatch.setattr(source_metadata, "enrich_sources", enrich)
    result = service.run_contract_research(req)
    assert len(result["sources"]) == 1
    assert result["sources"][0]["publishedAt"] == "2026-10-14T08:00:00Z"


def test_brave_news_does_not_fake_publication_date(monkeypatch):
    def fake_get(url, headers, params, timeout):
        assert url == news_search.BRAVE_NEWS_URL
        assert params["freshness"].endswith("to" + datetime.now(timezone.utc).date().isoformat())
        class Response:
            def raise_for_status(self):
                pass
            def json(self):
                return {"results": [
                    {"title": "New car announcement", "url": "https://ev.example/car",
                     "page_age": "2026-11-03T12:00:00Z"}
                ]}
        return Response()
    monkeypatch.setenv("BRAVE_SEARCH_API_KEY", "test-brave-key")
    monkeypatch.setattr(news_search.requests, "get", fake_get)
    articles = news_search.search_news("electric vehicles", 7, 10)
    assert len(articles) == 1
    assert articles[0]["published_at"] is None
    assert articles[0]["publication_date_kind"] == "discovery"
