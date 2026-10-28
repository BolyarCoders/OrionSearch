import copy
import json
from datetime import date

import pytest
from fastapi.testclient import TestClient

from api import main
from api.contract import CONTRACT_ROOT, ContractError, canonical_url, validate_request, validate_result
from api.discovery import build_result, execution_window

client = TestClient(main.app)
HEADERS = {"Authorization": "Bearer test-secret"}


def test_international_hosts_keep_idna_identity_instead_of_rewriting_sharp_s_to_ss():
    assert canonical_url("https://faß.de/story#fragment") == "https://xn--fa-hia.de/story"


@pytest.fixture
def request_body(monkeypatch):
    monkeypatch.setenv("ORION_WORKER_API_KEY", "test-secret")
    monkeypatch.delenv("ORION_WORKER_PREVIOUS_API_KEY", raising=False)
    monkeypatch.delenv("ORION_ALLOW_PROVISIONAL_CONTRACT", raising=False)
    return json.loads((CONTRACT_ROOT / "v1/request.json").read_text(encoding="utf-8"))


def discovery(request):
    return build_result(request, ["research methods"], ["google_news"], [
        {"url": "https://EXAMPLE.com:443/source#fragment", "title": "Discovered source", "language": "en"},
        {"url": "https://example.com/source", "title": "Duplicate", "language": "en"},
    ], today=date(2026, 10, 28))


def test_producer_matches_shared_discovery_fixture(request_body):
    produced = discovery(request_body)
    expected = json.loads((CONTRACT_ROOT / "v1/discovery-result.json").read_text(encoding="utf-8"))
    assert produced == expected
    assert validate_result(produced, request_body)
    assert not produced["findings"]
    assert all("capture" not in source for source in produced["sources"])


def test_full_fixtures_pass_shared_schemas(request_body):
    assert validate_request(request_body)
    result = json.loads((CONTRACT_ROOT / "v1/result.json").read_text(encoding="utf-8"))
    assert validate_result(result, request_body)


def test_real_route_uses_versioned_result_not_prototype_jobs(request_body, monkeypatch):
    monkeypatch.setattr(main, "run_contract_research", discovery)
    before = copy.deepcopy(main.jobs)
    response = client.post("/research", headers=HEADERS, json=request_body)
    assert response.status_code == 200
    assert response.headers["cache-control"] == "no-store"
    assert response.json() == discovery(request_body)
    assert main.jobs == before


@pytest.mark.parametrize("headers", [{}, {"X-API-Key": "test-secret"}, {"Authorization": "Bearer wrong"}, {"Authorization": "Basic test-secret"}])
def test_authentication_is_bearer_only(request_body, headers):
    response = client.post("/research", headers=headers, json=request_body)
    assert response.status_code == 401
    assert response.headers["www-authenticate"] == "Bearer"


def test_rotation_and_missing_config(request_body, monkeypatch):
    monkeypatch.setenv("ORION_WORKER_PREVIOUS_API_KEY", "previous-secret")
    monkeypatch.setattr(main, "run_contract_research", discovery)
    assert client.post("/research", headers={"Authorization": "Bearer previous-secret"}, json=request_body).status_code == 200
    monkeypatch.delenv("ORION_WORKER_API_KEY")
    assert client.post("/research", headers=HEADERS, json=request_body).status_code == 503


@pytest.mark.parametrize("problem", ["version", "extra", "uuid", "dates", "languages", "domains", "empty"])
def test_invalid_input_fails_before_execution(request_body, monkeypatch, problem):
    def forbidden(_):
        pytest.fail("Invalid request reached the provider")
    monkeypatch.setattr(main, "run_contract_research", forbidden)
    if problem == "version": request_body["contractVersion"] = "unknown-v9"
    elif problem == "extra": request_body["agentEndpoint"] = "https://attacker.example"
    elif problem == "uuid": request_body["researchRunId"] = "not-a-uuid"
    elif problem == "dates": request_body["settings"].update(dateFrom="2026-10-28", dateTo="2026-10-01")
    elif problem == "languages": request_body["settings"]["languages"] = ["en", "en"]
    elif problem == "domains": request_body["settings"]["excludedDomains"] = ["https://invalid.example"]
    elif problem == "empty": request_body["objective"] = "   "
    assert client.post("/research", headers=HEADERS, json=request_body).status_code == 400


def test_body_limits_and_duplicate_fields(request_body):
    response = client.post("/research", headers={**HEADERS, "Content-Type": "application/json"}, content=b"x" * 262145)
    assert response.status_code == 413
    assert client.post("/research", headers=HEADERS, content="not json").status_code == 415
    body = json.dumps(request_body)[:-1] + ',"contractVersion":"duplicate"}'
    assert client.post("/research", headers={**HEADERS, "Content-Type": "application/json"}, content=body).status_code == 400
    assert client.post("/research", headers={**HEADERS, "Content-Type": "application/json"}, content=json.dumps(request_body).encode("utf-16")).status_code == 400


def test_valid_dotnet_escaped_unicode_and_maximum_domain_lists_fit_the_request_budget(request_body, monkeypatch):
    request_body.update(title="界" * 200, description="界" * 10000, objective="界" * 2000)
    request_body["settings"].update(
        preferredDomains=[f"{i:02d}" + "a" * 61 + "." + "a" * 63 + "." + "a" * 63 + "." + "a" * 61 for i in range(100)],
        excludedDomains=[f"{i:02d}" + "b" * 61 + "." + "b" * 63 + "." + "b" * 63 + "." + "b" * 61 for i in range(100)],
        config={"value": "x" * 12000},
    )
    body = json.dumps(request_body, ensure_ascii=True)
    assert len(body.encode("utf-8")) > 131072
    monkeypatch.setattr(main, "run_contract_research", lambda payload: {
        "contractVersion": payload["contractVersion"], "researchRunId": payload["researchRunId"], "queries": [], "sources": [], "findings": []
    })
    assert client.post("/research", headers={**HEADERS, "Content-Type": "application/json"}, content=body).status_code == 200


def test_provisional_requires_explicit_opt_in(request_body, monkeypatch):
    request_body["contractVersion"] = "orion.research.provisional.v1"
    monkeypatch.setattr(main, "run_contract_research", discovery)
    assert client.post("/research", headers=HEADERS, json=request_body).status_code == 400
    monkeypatch.setenv("ORION_ALLOW_PROVISIONAL_CONTRACT", "true")
    assert client.post("/research", headers=HEADERS, json=request_body).status_code == 200


@pytest.mark.parametrize("failure,status", [(RuntimeError("private provider body"), 503), (ContractError("private settings"), 422), (main.ResearchBusy(), 503)])
def test_errors_are_sanitized(request_body, monkeypatch, failure, status):
    def fail(_): raise failure
    monkeypatch.setattr(main, "run_contract_research", fail)
    response = client.post("/research", headers=HEADERS, json=request_body)
    assert response.status_code == status
    assert "private" not in response.text


def test_wrong_run_and_oversized_output_are_not_successes(request_body, monkeypatch):
    result = discovery(request_body)
    result["researchRunId"] = "33333333-3333-4333-8333-333333333333"
    monkeypatch.setattr(main, "run_contract_research", lambda _: result)
    assert client.post("/research", headers=HEADERS, json=request_body).status_code == 422
    oversized = json.loads((CONTRACT_ROOT / "v1/result.json").read_text(encoding="utf-8"))
    oversized["sources"][0]["capture"]["metadata"] = {"padding": "x" * 2097152}
    monkeypatch.setattr(main, "run_contract_research", lambda _: oversized)
    assert client.post("/research", headers=HEADERS, json=request_body).status_code == 422


def test_filters_preserve_unknown_dates_and_apply_explicit_constraints(request_body):
    settings = request_body["settings"]
    settings.update(languages=[], preferredDomains=["preferred.example"], excludedDomains=["excluded.example"], config={"maxArticles": 1})
    articles = [{"url": "https://other.example/a"}, {"url": "https://preferred.example/a"}, {"url": "https://sub.excluded.example/a"}]
    result = build_result(request_body, ["query"], ["google_news"], articles)
    assert [source["canonicalUrl"] for source in result["sources"]] == ["https://preferred.example/a"]
    assert "publishedAt" not in result["sources"][0]
    settings.update(dateFrom="2026-10-01", dateTo="2026-10-28")
    assert not build_result(request_body, ["query"], ["google_news"], articles, today=date(2026, 10, 28))["sources"]


def test_unsupported_window_and_invalid_execution_config_are_not_silently_changed(request_body):
    request_body["settings"]["dateFrom"] = "2025-01-01"
    with pytest.raises(ContractError): execution_window(request_body, today=date(2026, 10, 28))
    request_body["settings"]["dateFrom"] = None
    request_body["settings"]["config"] = {"maxArticles": True}
    with pytest.raises(ContractError): execution_window(request_body)


def test_publisher_filters_do_not_treat_google_news_as_the_article_publisher(request_body):
    request_body["settings"].update(languages=[], excludedDomains=["excluded.example"])
    articles = [
        {"url": "https://news.google.com/wrapper-a", "publisher_name": "Excluded", "publisher_url": "https://excluded.example"},
        {"url": "https://news.google.com/unknown-publisher"},
        {"url": "https://news.google.com/wrapper-b", "publisher_name": "Allowed", "publisher_url": "https://allowed.example"},
    ]
    result = build_result(request_body, ["query"], ["google_news"], articles)
    assert len(result["sources"]) == 1
    assert result["sources"][0]["publisher"] == {"name": "Allowed", "domain": "allowed.example"}


def test_prototype_queue_is_disabled_in_integrated_mode(request_body, monkeypatch):
    monkeypatch.delenv("ORION_ENABLE_PROTOTYPE_API", raising=False)
    assert client.post("/prototype/research/jobs", json={"topic": "Topic"}, headers={"X-API-Key": "test-secret"}).status_code == 404
    assert client.post("/research/jobs", json={"topic": "Topic"}, headers=HEADERS).status_code == 404


@pytest.mark.parametrize("kind", ["updated", "discovery"])
def test_update_and_discovery_times_do_not_become_article_publication_dates(request_body, kind):
    request_body["settings"]["languages"] = []
    articles = [{"url": "https://example.com/source", "published_at": "2026-10-28T10:00:00Z", "publication_date_kind": kind}]
    result = build_result(request_body, ["query"], ["google_news"], articles, today=date(2026, 10, 28))
    assert "publishedAt" not in result["sources"][0]
    request_body["settings"].update(dateFrom="2026-10-28", dateTo="2026-10-28")
    assert not build_result(request_body, ["query"], ["google_news"], articles, today=date(2026, 10, 28))["sources"]
