"""Regression tests for the complete source-based research briefing."""
import json
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[1]
AGENT = ROOT / "agent"
if str(AGENT) not in sys.path:
    sys.path.insert(0, str(AGENT))

import ai_agent
from api import briefing_content


def article(i, excerpt=None):
    item = {
        "title": f"EV model announcement {i}",
        "url": f"https://news.example/story/{i}",
        "source": "Example News",
        "published_at": f"2026-11-{(i % 28) + 1:02d}T10:00:00Z",
        "summary": f"New battery vehicle release number {i}",
    }
    if excerpt:
        item["article_excerpt"] = excerpt
    return item


def test_short_briefing_uses_every_source_with_complete_link_index(monkeypatch):
    prompts = []
    def answer(client, prompt):
        prompts.append(prompt)
        return "## Основни тенденции\nНови EV модели [1], [2]."
    monkeypatch.setattr(ai_agent, "ask_ai", answer)
    result = ai_agent.analyze_articles(
        object(), {"objective": "electric vehicles"}, 7,
        [article(1, "Detailed launch with pricing"), article(2)]
    )
    assert len(prompts) == 1
    assert "Detailed launch with pricing" in prompts[0]
    assert "page_excerpt" in prompts[0]
    assert "search_snippet_only" in prompts[0]
    assert "IN BULGARIAN" in prompts[0]
    assert "Източници (2)" in result
    assert "[1] EV model announcement 1" in result
    assert "[2] EV model announcement 2" in result


def test_long_briefing_covers_all_articles_in_batches(monkeypatch):
    prompts = []
    def answer(client, prompt):
        prompts.append(prompt)
        return "Свързани тенденции [1], [16]."
    monkeypatch.setattr(ai_agent, "ask_ai", answer)
    sources = [article(i) for i in range(1, 18)]
    report = ai_agent.analyze_articles(object(), "electric vehicles", 30, sources)
    assert len(prompts) == 4  # 3 groups of 8, then synthesis
    for i in range(1, 18):
        assert any(f'"source_id": {i}' in prompt for prompt in prompts[:3])
        assert f"[{i}] EV model announcement {i} — https://news.example/story/{i}" in report
    assert "NUMBERED SOURCE NOTES" in prompts[-1]


def test_empty_sources_report_is_clear_and_nonempty(monkeypatch):
    monkeypatch.setattr(ai_agent, "ask_ai", lambda *args: pytest.fail("No sources should skip AI"))
    result = ai_agent.analyze_articles(object(), "electric vehicles", 7, [])
    assert "Не са намерени" in result


def test_source_text_retrieval_falls_back_without_dropping_any_source():
    calls = []
    def fake_fetch(url, excluded, deadline):
        calls.append((url, excluded))
        if url.endswith("/2"):
            raise ValueError("Unavailable")
        return (b"<html><body><main><p>Detailed electric vehicle launch.</p></main></body></html>",
                "text/html", url)
    results = briefing_content.enrich_briefing_articles(
        [article(1), article(2)], ["blocked.example"], fetch=fake_fetch
    )
    assert len(results) == 2
    assert "Detailed electric vehicle launch" in results[0]["article_excerpt"]
    assert "article_excerpt" not in results[1]
    assert len(calls) == 2
    assert all(excluded == ["blocked.example"] for _, excluded in calls)


def test_article_text_fetch_has_finite_cap():
    def fake_fetch(url, excluded, deadline):
        return b"Article " + b"A" * 5000, "text/plain", url
    sources = [article(i) for i in range(1, 28)]
    results = briefing_content.enrich_briefing_articles(sources, [], fetch=fake_fetch)
    assert len(results) == 27
    assert all(len(x["article_excerpt"]) <= 2400 for x in results[:20])
    assert all("article_excerpt" not in x for x in results[20:])
