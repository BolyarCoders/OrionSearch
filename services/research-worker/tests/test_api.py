import pytest
from fastapi.testclient import TestClient

from api import main
from api.models import ResearchResult

client = TestClient(main.app)
HEADERS = {"X-API-Key": "test-secret"}


def fake_result(request):
    return ResearchResult(
        topic=request.topic,
        days=request.days,
        queries=[request.topic],
        sources=["google_news"],
        total_articles=1,
        articles=[{"title": "Sample article", "source": "Google News"}],
        report="## Summary\nA test report.",
        errors={},
        generated_at="2026-10-21T10:00:00Z",
    )


def test_health():
    assert client.get("/health").json()["status"] == "ok"


def test_authentication(monkeypatch):
    monkeypatch.setenv("ORION_WORKER_API_KEY", "test-secret")
    assert (
        client.post("/prototype/research", json={"topic": "Artificial Intelligence"}).status_code
        == 401
    )
    assert (
        client.post(
            "/prototype/research",
            headers={"X-API-Key": "wrong"},
            json={"topic": "Artificial Intelligence"},
        ).status_code
        == 401
    )


def test_input_validation(monkeypatch):
    monkeypatch.setenv("ORION_WORKER_API_KEY", "test-secret")
    assert (
        client.post(
            "/prototype/research", headers=HEADERS, json={"topic": "AI", "days": 999}
        ).status_code
        == 422
    )


def test_sync_research(monkeypatch):
    monkeypatch.setenv("ORION_WORKER_API_KEY", "test-secret")
    monkeypatch.setattr(main, "run_research", fake_result)
    response = client.post(
        "/prototype/research",
        headers=HEADERS,
        json={"topic": "Artificial Intelligence", "days": 7},
    )
    assert response.status_code == 200
    assert response.json()["total_articles"] == 1
    assert response.json()["report"].startswith("## Summary")


def test_background_job(monkeypatch):
    monkeypatch.setenv("ORION_WORKER_API_KEY", "test-secret")
    monkeypatch.setattr(main, "run_research", fake_result)
    response = client.post(
        "/prototype/research/jobs", headers=HEADERS, json={"topic": "Artificial Intelligence"}
    )
    assert response.status_code == 202
    job = client.get(response.json()["status_url"], headers=HEADERS)
    assert job.status_code == 200
    assert job.json()["status"] == "completed"
    assert job.json()["result"]["total_articles"] == 1


def test_unknown_job(monkeypatch):
    monkeypatch.setenv("ORION_WORKER_API_KEY", "test-secret")
    assert client.get("/prototype/research/jobs/no-such-job", headers=HEADERS).status_code == 404


def test_background_failure(monkeypatch):
    monkeypatch.setenv("ORION_WORKER_API_KEY", "test-secret")

    def fail(request):
        raise RuntimeError("Simulated internal failure")

    monkeypatch.setattr(main, "run_research", fail)
    response = client.post(
        "/prototype/research/jobs", headers=HEADERS, json={"topic": "Robotics"}
    )
    status = client.get(response.json()["status_url"], headers=HEADERS).json()
    assert status["status"] == "failed"
    assert "Simulated internal failure" not in status["error"]

@pytest.fixture(autouse=True)
def prototype_mode(monkeypatch):
    monkeypatch.setenv("ORION_ENABLE_PROTOTYPE_API", "true")
