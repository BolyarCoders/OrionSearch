"""Regression cases for the irrelevant sources shown for electric vehicles."""
import json
import sys
from pathlib import Path

import pytest

AGENT = Path(__file__).resolve().parents[1] / "agent"
if str(AGENT) not in sys.path:
    sys.path.insert(0, str(AGENT))

import relevance
from api.contract import CONTRACT_ROOT, validate_result
from api.discovery import build_result


def article(title, summary="", url=None):
    return {
        "title": title, "summary": summary, "source": "Web Search",
        "url": url or "https://source.example/" + str(abs(hash(title))),
        "publication_date_kind": "discovery", "published_at": None
    }


@pytest.fixture
def noisy_ev_sources():
    return [
        article("List of battery electric vehicles", "Battery electric cars"),
        article("Civic Information API | Google for Developers", "Google API civic information endpoints"),
        article("Use Places APIs and Geocoding", "Google Maps geocoding documentation"),
        article("EV battery trends and charging infrastructure", "Electric cars, battery and charging"),
        article("Electric vehicle", "Encyclopedia overview of electric cars"),
        article("Divisions | Civic Information API", "Google civic divisions"),
    ]


def test_semantic_relevance_filters_unrelated_google_api_results(noisy_ev_sources):
    prompts = []

    def semantic(client, prompt):
        prompts.append(prompt)
        return '{"scores":[3,0,0,3,2,0]}'

    results = relevance.select_relevant_articles(
        object(), "electric vehicles", ["electric vehicles"], noisy_ev_sources,
        ask_ai=semantic
    )
    assert len(results) == 3
    assert all("Civic" not in r["title"] and "Geocoding" not in r["title"] for r in results)
    assert results[0]["title"] == "List of battery electric vehicles"
    assert results[-1]["title"] == "Electric vehicle"
    assert "electric vehicles" in prompts[0]


def test_fallback_drops_unrelated_results_when_semantic_model_fails(noisy_ev_sources):
    def fail(client, prompt):
        raise RuntimeError("AI is unavailable")

    results = relevance.select_relevant_articles(
        object(), "electric vehicles", ["electric vehicles"], noisy_ev_sources,
        ask_ai=fail
    )
    assert results
    assert all("Civic" not in r["title"] and "Geocoding" not in r["title"] for r in results)
    assert any("battery electric vehicles" in r["title"] for r in results)


def test_relevance_can_preserve_topic_synonyms():
    sources = [
        article("Tesla launches an affordable new EV", "New Tesla battery car"),
        article("Civic Information API for mapping", "Google documentation"),
    ]
    results = relevance.select_relevant_articles(
        object(), "electric vehicles", ["electric vehicles"], sources,
        ask_ai=lambda client, prompt: '{"scores":[3,0]}'
    )
    assert [r["title"] for r in results] == ["Tesla launches an affordable new EV"]


def test_source_scoring_rejects_invalid_provider_output(noisy_ev_sources):
    for response in ('{"scores":[3]}', '{"scores":[true,0,0,0,0,0]}', '{}', 'not json'):
        results = relevance.select_relevant_articles(
            object(), "electric vehicles", ["electric vehicles"], noisy_ev_sources,
            ask_ai=lambda client, prompt: response
        )
        assert results
        assert all("Civic" not in r["title"] for r in results)


def test_different_topic_and_languages_do_not_use_ev_specific_rules():
    sources = [
        article("Новите тенденции в модата през 2026 г.", "Есенна мода"),
        article("Избор на зимни гуми за автомобили", "Автомобилни гуми"),
        article("How to style streetwear", "Fashion and clothing"),
    ]
    results = relevance.select_relevant_articles(
        object(), "fashion trends", ["fashion trends", "тенденции в модата"],
        sources, ask_ai=lambda client, prompt: '{"scores":[3,0,3]}'
    )
    assert [r["title"] for r in results] == [sources[0]["title"], sources[2]["title"]]


def test_result_contains_only_scored_sources_in_ranked_order(noisy_ev_sources):
    accepted = relevance.select_relevant_articles(
        object(), "electric vehicles", ["electric vehicles"], noisy_ev_sources,
        ask_ai=lambda client, prompt: '{"scores":[2,0,0,3,2,0]}'
    )
    request = json.loads((CONTRACT_ROOT / "v1/request.json").read_text(encoding="utf-8"))
    request["settings"]["config"] = {"maxArticles": 10}
    result = build_result(request, ["electric vehicles"], ["web_search"], accepted)
    assert len(result["sources"]) == 3
    assert "EV battery trends" in result["sources"][0]["title"]
    assert all("Civic" not in s["title"] for s in result["sources"])
    assert validate_result(result, request)
