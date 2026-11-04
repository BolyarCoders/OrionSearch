"""Dated topic-based news discovery with optional official Brave Search API."""
import logging
import os
from datetime import datetime, timedelta, timezone
from urllib.parse import urlsplit

import requests
from ddgs import DDGS

logger = logging.getLogger("orionsearch.news_search")
BRAVE_NEWS_URL = "https://api.search.brave.com/res/v1/news/search"


def _valid_url(url):
    if not isinstance(url, str):
        return False
    try:
        parts = urlsplit(url)
        return parts.scheme in ("https", "http") and bool(parts.hostname)
    except ValueError:
        return False


def _parse_date(raw):
    if not isinstance(raw, str):
        return None
    try:
        value = datetime.fromisoformat(raw.replace("Z", "+00:00"))
        if value.tzinfo is None:
            if len(raw) != 10:
                return None
            value = value.replace(tzinfo=timezone.utc)
        value = value.astimezone(timezone.utc)
        if value > datetime.now(timezone.utc):
            return None
        return value.isoformat()
    except (ValueError, OverflowError, TypeError):
        return None


def _brave_news(query, days, limit, key):
    """Brave freshness uses page age (possibly updated), not proven publication."""
    today = datetime.now(timezone.utc).date()
    start = today - timedelta(days=days - 1)
    response = requests.get(
        BRAVE_NEWS_URL,
        headers={"X-Subscription-Token": key, "Accept": "application/json"},
        params={
            "q": " ".join(query.split()[:70])[:500],
            "count": min(limit, 50),
            "freshness": f"{start.isoformat()}to{today.isoformat()}",
        },
        timeout=(3, 12),
    )
    response.raise_for_status()
    data = response.json()
    return data.get("results", [])


def search_news(query, days, limit):
    """Find public news on any topic without inventing publication dates.

    When an official Brave key is set, use its dedicated news index first.
    Brave's page_age is *not* treated as publication date: metadata enrichment
    must establish a true date for those results to be included downstream.
    Without an API key, DDGS.news provides dated news candidates.
    """
    key = os.getenv("BRAVE_SEARCH_API_KEY", "").strip()
    results = None
    brave = bool(key)
    if brave:
        try:
            results = _brave_news(query, days, limit, key)
        except (requests.RequestException, ValueError, TypeError):
            logger.warning("Official news provider unavailable; trying metasearch")
            brave = False

    if results is None:
        results = DDGS(timeout=12).news(query, max_results=limit)

    articles = []
    for rank, item in enumerate(list(results or [])[:limit]):
        if not isinstance(item, dict):
            continue
        url, title = item.get("url"), item.get("title")
        if not _valid_url(url) or not isinstance(title, str) or not title.strip():
            continue
        if brave:
            # Brave's freshness index may reflect modifications, not publication.
            # Leave the publication date absent until HTML metadata is examined.
            stamp, kind = None, "discovery"
        else:
            stamp = _parse_date(item.get("date"))
            if stamp is None:
                continue
            kind = "published"
        articles.append({
            "title": title.strip()[:1000],
            "url": url,
            "source": "News Search",
            "published_at": stamp,
            "publication_date_kind": kind,
            "summary": str(item.get("description") or item.get("body") or "")[:500],
            "search_rank": rank,
        })
    return articles
