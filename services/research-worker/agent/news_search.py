"""Dated news discovery; unlike general web results, these have publish dates."""
import logging
from datetime import datetime, timezone
from urllib.parse import urlsplit

from ddgs import DDGS

logger = logging.getLogger("orionsearch.news_search")


def search_news(query, days, limit):
    """Find published reporting about any topic.

    Do not fabricate dates. The service enforces the user's exact date range
    after metadata enrichment.
    """
    results = DDGS(timeout=12).news(query, max_results=limit)
    articles = []
    for rank, item in enumerate(list(results or [])[:limit]):
        if not isinstance(item, dict):
            continue
        url, title, raw_date = item.get("url"), item.get("title"), item.get("date")
        if not isinstance(url, str) or not isinstance(title, str) or not isinstance(raw_date, str):
            continue
        try:
            parsed = urlsplit(url)
            if parsed.scheme not in ("https", "http") or not parsed.hostname:
                continue
            stamp = datetime.fromisoformat(raw_date.replace("Z", "+00:00"))
            if stamp.tzinfo is None:
                # Date-only strings can be interpreted as UTC calendar dates.
                if len(raw_date) != 10:
                    continue
                stamp = stamp.replace(tzinfo=timezone.utc)
            stamp = stamp.astimezone(timezone.utc)
            if stamp > datetime.now(timezone.utc):
                continue
        except (ValueError, OverflowError, TypeError):
            continue
        articles.append({
            "title": title.strip()[:1000],
            "url": url,
            "source": "News Search",
            "published_at": stamp.isoformat(),
            "publication_date_kind": "published",
            "summary": str(item.get("body") or item.get("description") or "")[:500],
            "search_rank": rank,
        })
    return articles
