import sys
import json
from types import ModuleType, SimpleNamespace

import pytest

from api import service
from api.models import ResearchRequest
from api.contract import CONTRACT_ROOT


@pytest.mark.parametrize("collection_fails", [False, True])
@pytest.mark.parametrize("capture_enabled", [False, True])
def test_integrated_service_uses_existing_planner_without_reports_or_second_job_store(monkeypatch, collection_fails, capture_enabled):
    calls = []
    from api import captures
    monkeypatch.setenv("ORION_CAPTURE_ENABLED", "true" if capture_enabled else "false")
    monkeypatch.setattr(captures, "CaptureUploader", lambda: SimpleNamespace(close=lambda: calls.append("storage-closed")))
    def enrich(result, payload, uploader):
        assert payload["researchRunId"] == result["researchRunId"]
        calls.append("captures")
        return result
    monkeypatch.setattr(captures, "enrich_captures", enrich)
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

    def collect(topic, days, queries, selected, *, max_articles):
        assert max_articles == 50
        return ([{"url": "https://example.com/source", "language": "en", "title": "Discovered"}],
                {"google_news": "private upstream body"} if collection_fails else {}, 1)

    engine = ModuleType("ai_agent")
    engine.SOURCES = {"google_news": object()}
    engine.plan_research = plan
    engine.collect_articles = collect
    collector = ModuleType("collector")
    collector.SOURCES = ("google_news",)
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
    assert calls == (["captures"] if capture_enabled and not collection_fails else []) + ["closed"] + (["storage-closed"] if capture_enabled else [])
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
    from datetime import datetime, timezone

    from api import service
    from api.models import ResearchRequest

    calls = []

    class FakeClient:
        def close(self):
            calls.append(("close",))

    def create_client():
        calls.append(("client",))
        return FakeClient()

    def plan(client, topic, days):
        calls.append(("plan", topic, days))
        return ["new AI models"], ["google_news"]

    def collect(topic, days, queries, sources, max_articles=10):
        calls.append(("collect", topic, days, queries, sources, max_articles))

        articles = [
            {
                "title": "Test article",
                "url": "https://example.com/article",
                "source": "Google News",
                "published_at": datetime.now(timezone.utc).isoformat(),
                "summary": "Test summary",
                "trends": [],
            }
        ]

        # New collector contract:
        # articles, errors, total_articles
        return articles, {}, 4

    def analyze(client, topic, days, articles):
        calls.append(("analyze", len(articles)))
        return "AI Report: Test result."

    monkeypatch.setenv("GEMINI_API_KEY", "fake-gemini-key")

    # Patch the already-imported agent used by service.py
    monkeypatch.setattr(service.ai_agent, "create_client", create_client)
    monkeypatch.setattr(service.ai_agent, "plan_research", plan)
    monkeypatch.setattr(service.ai_agent, "collect_articles", collect)
    monkeypatch.setattr(service.ai_agent, "analyze_articles", analyze)

    request = ResearchRequest(topic="Artificial Intelligence", days=7, max_articles=1)

    response = service.run_research(request)

    assert response.topic == "Artificial Intelligence"
    assert response.total_articles == 4
    assert len(response.articles) == 1
    assert response.report == "AI Report: Test result."
    assert response.errors == {}

    assert calls == [
        ("client",),
        ("plan", "Artificial Intelligence", 7),
        (
            "collect",
            "Artificial Intelligence",
            7,
            ["new AI models"],
            ["google_news"],
            1,
        ),
        ("analyze", 1),
        ("close",),
    ]
