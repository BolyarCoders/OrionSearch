"""Topic-independent web discovery.

Unlike the optional RSS adapters, these searches are not restricted to a
preselected set of publishers or subject areas.
"""
import logging
import os
from urllib.parse import urlsplit

import requests
from ddgs import DDGS

logger = logging.getLogger("orionsearch.web_search")
BRAVE_URL = "https://api.search.brave.com/res/v1/web/search"


def _brave_results(query, limit, key):
    response = requests.get(
        BRAVE_URL,
        headers={"X-Subscription-Token": key, "Accept": "application/json"},
        # Brave rejects requests above 600 characters or 75 words.
        params={"q": " ".join(query.split()[:70])[:500], "count": min(limit, 20), "safesearch": "moderate"},
        timeout=(3, 10),
    )
    response.raise_for_status()
    return response.json().get("web", {}).get("results", [])


def _ddgs_results(query, limit):
    # DDGS searches general web indexes, not the Hacker News or AI RSS feeds.
    # Do not set a time limit: ordinary web pages may not declare a publish date.
    return DDGS(timeout=10).text(query, max_results=limit)


def search_web(query, days, limit):
    """Discover relevant public pages from any topic; never invent publication dates.

    days is accepted to match the collector adapter signature. Date filtering
    applies only to sources with verified publication dates, not generic pages.
    """
    key = os.getenv("BRAVE_SEARCH_API_KEY", "").strip()
    results = None
    if key:
        try:
            results = _brave_results(query, limit, key)
        except requests.RequestException:
            logger.warning("Brave web search unavailable; using the metasearch fallback")

    if results is None:
        results = _ddgs_results(query, limit)

    articles = []
    for rank, item in enumerate(list(results or [])[:limit]):
        if not isinstance(item, dict):
            continue
        url = item.get("url") or item.get("href")
        title = item.get("title")
        if not isinstance(url, str) or not isinstance(title, str):
            continue
        try:
            parsed = urlsplit(url)
            if parsed.scheme.lower() not in ("http", "https") or not parsed.hostname:
                continue
        except ValueError:
            continue
        title = title.strip()
        if not title:
            continue
        articles.append({
            "title": title[:1000],
            "url": url,
            "source": "Web Search",
            "search_rank": rank,
            "published_at": None,
            "publication_date_kind": "discovery",
            "summary": str(item.get("description") or item.get("body") or "")[:500],
        })
    return articles
