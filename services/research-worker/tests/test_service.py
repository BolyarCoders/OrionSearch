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
