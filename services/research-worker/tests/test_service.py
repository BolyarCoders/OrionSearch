import sys
import json
from types import ModuleType, SimpleNamespace

import pytest

from api import service
from api.models import ResearchRequest
from api.contract import CONTRACT_ROOT


@pytest.mark.parametrize("collection_fails", [False, True])
def test_integrated_service_uses_existing_planner_without_reports_or_second_job_store(monkeypatch, collection_fails):
    calls = []
    request = json.loads((CONTRACT_ROOT / "v1/request.json").read_text(encoding="utf-8"))
    request["settings"]["config"] = {"apiKey": "untrusted-override", "agentEndpoint": "https://untrusted.example"}

    class Client:
        def __init__(self, api_key, http_options):
            assert api_key == "test-provider-key"
            assert http_options.timeout == 60000
            assert http_options.retry_options.attempts == 1
        def close(self): calls.append("closed")

    def plan(client, topic, days):
        assert request["title"] in topic and request["objective"] in topic
        assert days == 7
        return ["research methods"], ["google_news"]

    def collect(topic, days, queries, selected, *, persist_output):
        assert persist_output is False
        return ([{"url": "https://example.com/source", "language": "en", "title": "Discovered"}],
                {"google_news": "private upstream body"} if collection_fails else {})

    engine = ModuleType("ai_agent")
    engine.SOURCES = {"google_news": object()}
    engine.plan_research = plan
    engine.collect_articles = collect
    collector = ModuleType("collector")
    collector.CONFIG = {"sources": {"google_news": True}}
    google = ModuleType("google")
    google.genai = SimpleNamespace(Client=Client)
    types = SimpleNamespace(HttpOptions=lambda **values: SimpleNamespace(**values), HttpRetryOptions=lambda **values: SimpleNamespace(**values))
    google.genai.types = types
    monkeypatch.setenv("GEMINI_API_KEY", "test-provider-key")
    for name, value in {"ai_agent": engine, "collector": collector, "google": google, "google.genai": google.genai, "google.genai.types": types}.items():
        monkeypatch.setitem(sys.modules, name, value)
    if collection_fails:
        with pytest.raises(RuntimeError, match="did not complete"):
            service.run_contract_research(request)
    else:
        result = service.run_contract_research(request)
        assert len(result["sources"]) == 1
        assert result["findings"] == []
    assert calls == ["closed"]
    assert not service.research_lock.locked()


def test_integrated_capacity_refuses_work_instead_of_creating_an_in_memory_queue(monkeypatch):
    monkeypatch.setenv("GEMINI_API_KEY", "test-provider-key")
    request = json.loads((CONTRACT_ROOT / "v1/request.json").read_text(encoding="utf-8"))
    with service.research_lock:
        with pytest.raises(service.ResearchBusy): service.run_contract_research(request)


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
