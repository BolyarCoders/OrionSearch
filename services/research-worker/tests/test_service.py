import sys
from types import ModuleType, SimpleNamespace

import pytest

from api import service
from api.models import ResearchRequest


def test_missing_gemini_key(monkeypatch):
    monkeypatch.delenv("GEMINI_API_KEY", raising=False)
    with pytest.raises(RuntimeError, match="GEMINI_API_KEY"):
        service.run_research(ResearchRequest(topic="Artificial Intelligence"))


def test_service_calls_existing_agent(monkeypatch):
    calls = []

    class FakeClient:
        def __init__(self, api_key):
            calls.append(("client", api_key))

        def close(self):
            calls.append(("close",))

    def plan(client, topic, days):
        calls.append(("plan", topic, days))
        return ["new AI models"], ["google_news"]

    def collect(topic, days, queries, sources):
        calls.append(("collect", topic, days, queries, sources))
        return [
            {
                "title": "Test article",
                "url": "https://example.com/article",
                "source": "Google News",
            }
        ], {}

    def analyze(client, topic, days, articles):
        calls.append(("analyze", len(articles)))
        return "## AI Report\nTest result."

    def save(topic, days, queries, articles, errors, report):
        calls.append(("save", topic))

    ai_agent = ModuleType("ai_agent")
    ai_agent.plan_research = plan
    ai_agent.collect_articles = collect
    ai_agent.analyze_articles = analyze
    ai_agent.save_report = save

    google = ModuleType("google")
    google.genai = SimpleNamespace(Client=FakeClient)

    monkeypatch.setenv("GEMINI_API_KEY", "fake-gemini-key")
    monkeypatch.setitem(sys.modules, "ai_agent", ai_agent)
    monkeypatch.setitem(sys.modules, "google", google)
    monkeypatch.setitem(sys.modules, "google.genai", google.genai)

    response = service.run_research(
        ResearchRequest(topic="Artificial Intelligence", max_articles=1)
    )

    assert response.topic == "Artificial Intelligence"
    assert response.total_articles == 1
    assert response.articles[0]["title"] == "Test article"
    assert response.report.startswith("## AI Report")
    assert ("plan", "Artificial Intelligence", 7) in calls
    assert ("save", "Artificial Intelligence") in calls
    assert calls[-1] == ("close",)
