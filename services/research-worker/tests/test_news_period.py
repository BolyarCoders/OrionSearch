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
