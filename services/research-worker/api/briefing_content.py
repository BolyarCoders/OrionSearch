"""Retrieve bounded public article text for AI research briefings.

This is best-effort enrichment, not a capture/version grant. The existing
isolated public fetcher protects against SSRF and enforces finite budgets.
All accepted sources remain part of the final briefing even if a page fails.
"""
import logging
import time
from concurrent.futures import ThreadPoolExecutor, as_completed

from api.captures import extract_text, fetch_isolated

logger = logging.getLogger("orionsearch.briefing")
MAX_ARTICLES_TO_FETCH = 20
MAX_EXCERPT = 2400
FETCH_BUDGET = 24


def enrich_briefing_articles(articles, excluded_domains, *, fetch=fetch_isolated):
    """Return new article dictionaries with optional source text excerpts.

    Never let an unreachable publisher block the research run, and never
    change the archived source metadata or invent facts from search snippets.
    """
    results = [dict(article) for article in articles]
    if not results:
        return results
    deadline = time.monotonic() + FETCH_BUDGET

    def retrieve(index):
        article = results[index]
        try:
            data, media_type, _ = fetch(article["url"], excluded_domains, deadline)
            text = extract_text(data, media_type)
            return index, text[:MAX_EXCERPT]
        except Exception:
            return index, None

    with ThreadPoolExecutor(max_workers=5) as pool:
        futures = [pool.submit(retrieve, index) for index in range(min(len(results), MAX_ARTICLES_TO_FETCH))]
        for future in as_completed(futures):
            index, excerpt = future.result()
            if excerpt:
                results[index]["article_excerpt"] = excerpt
    logger.info(
        "Article text available for %d/%d briefing sources",
        sum(bool(article.get("article_excerpt")) for article in results),
        len(results),
    )
    return results
