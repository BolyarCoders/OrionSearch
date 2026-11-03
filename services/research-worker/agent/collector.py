import html
import logging
import os
import re

from concurrent.futures import ThreadPoolExecutor, as_completed
from datetime import datetime, timedelta, timezone
from time import perf_counter
from urllib.parse import urlencode

import feedparser
import requests

logger = logging.getLogger("orionsearch.collector")


# ============================================================
# CONFIGURATION
# ============================================================

SOURCES = (
    "google_news",
    "hacker_news",
    "arxiv",
    "hugging_face",
)

TIMEOUT = (3, 8)
MAX_WORKERS = 6


# Original OrionSearch trend categories.
# Stored directly in Python instead of config.json.

TREND_CATEGORIES = {
    "AI agents": [
        "agentic",
        "ai agents",
        "agent framework",
        "multi-agent",
        "tool use",
    ],
    "Language models": [
        "llm",
        "large language model",
        "reasoning model",
        "transformer",
    ],
    "Multimodal AI": [
        "multimodal",
        "vision language",
        "video generation",
        "image generation",
    ],
    "AI hardware": [
        "gpu",
        "inference chip",
        "accelerator",
        "compute infrastructure",
    ],
    "Robotics": [
        "robotics",
        "humanoid",
        "embodied ai",
    ],
    "Open source AI": [
        "open source model",
        "open-weight",
        "open weights",
        "hugging face",
    ],
}


# ============================================================
# VALIDATION
# ============================================================


def get_feed_limit():
    raw = os.getenv("RESEARCH_FEED_LIMIT", "12")

    try:
        limit = int(raw)
    except (TypeError, ValueError) as exc:
        raise ValueError(
            "RESEARCH_FEED_LIMIT must be an integer from 1 to 100"
        ) from exc

    if not 1 <= limit <= 100:
        raise ValueError("RESEARCH_FEED_LIMIT must be an integer from 1 to 100")

    return limit


def get_selected_sources():
    raw = os.getenv("RESEARCH_SOURCES")

    if raw is None:
        return ["google_news", "hacker_news"]

    sources = [source.strip() for source in raw.split(",")]

    unknown = set(sources) - set(SOURCES)

    if not sources or any(not source for source in sources) or unknown:
        raise ValueError(
            f"Invalid RESEARCH_SOURCES={raw!r}. " f"Allowed: {', '.join(SOURCES)}"
        )

    return list(dict.fromkeys(sources))


def validate_limits(days, max_articles=None):
    if type(days) is not int or not 1 <= days <= 90:
        raise ValueError("days must be between 1 and 90")

    if max_articles is not None:
        if type(max_articles) is not int or not 1 <= max_articles <= 50:
            raise ValueError("max_articles must be between 1 and 50")


# ============================================================
# TEXT PROCESSING
# ============================================================


def clean_text(value):
    return html.unescape(re.sub(r"<[^>]+>", "", value or "")).strip()


def classify_trends(article):
    """
    Classify articles using title and summary keywords.

    A single article can belong to multiple trend categories.

    This function does not perform AI requests or create files.
    """

    text = (
        f"{article.get('title') or ''} " f"{article.get('summary') or ''}"
    ).casefold()

    matched_categories = []

    for category, keywords in TREND_CATEGORIES.items():
        if any(keyword.casefold() in text for keyword in keywords):
            matched_categories.append(category)

    return matched_categories


# ============================================================
# RSS COLLECTOR
# ============================================================


def get_rss(url, source, limit):
    response = requests.get(
        url,
        timeout=TIMEOUT,
        headers={"User-Agent": "OrionSearch/1.0"},
    )

    response.raise_for_status()

    feed = feedparser.parse(response.content)
    articles = []

    for entry in feed.entries[:limit]:
        date = entry.get("published_parsed") or entry.get("updated_parsed")

        published_at = (
            datetime(*date[:6], tzinfo=timezone.utc).isoformat() if date else None
        )

        articles.append(
            {
                "title": clean_text(entry.get("title")),
                "url": entry.get("link", ""),
                "source": source,
                "published_at": published_at,
                "publication_date_kind": "published" if entry.get("published_parsed") else "updated",
                "summary": clean_text(entry.get("summary"))[:500],
            }
        )
        publisher = entry.get("source") or {}
        if publisher.get("href") and publisher.get("title"):
            articles[-1]["publisher_url"] = publisher["href"]
            articles[-1]["publisher_name"] = publisher["title"]

    return articles


# ============================================================
# GOOGLE NEWS
# ============================================================


def search_google_news(query, days, limit):
    params = {
        "q": f"{query} when:{days}d",
        "hl": "en-US",
        "gl": "US",
        "ceid": "US:en",
    }

    url = "https://news.google.com/rss/search?" + urlencode(params)

    return get_rss(url, "Google News", limit)


# ============================================================
# HACKER NEWS
# ============================================================


def search_hacker_news(query, days, limit):
    since = int((datetime.now(timezone.utc) - timedelta(days=days)).timestamp())

    params = {
        "query": query,
        "tags": "story",
        "hitsPerPage": limit,
        "numericFilters": f"created_at_i>{since}",
    }

    response = requests.get(
        "https://hn.algolia.com/api/v1/search_by_date",
        params=params,
        timeout=TIMEOUT,
    )

    response.raise_for_status()

    articles = []

    for item in response.json().get("hits", []):
        article_id = item.get("objectID", "")

        articles.append(
            {
                "title": item.get("title") or "",
                "url": (
                    item.get("url")
                    or ("https://news.ycombinator.com/" f"item?id={article_id}")
                ),
                "source": "Hacker News",
                "published_at": item.get("created_at"),
                "publication_date_kind": "discovery" if item.get("url") else "published",
                "summary": "",
            }
        )

    return articles


# ============================================================
# ARXIV
# ============================================================


def search_arxiv(limit):
    return get_rss(
        "https://rss.arxiv.org/rss/cs.AI+cs.LG+cs.CL",
        "arXiv",
        limit,
    )


# ============================================================
# HUGGING FACE
# ============================================================


def search_hugging_face(limit):
    return get_rss(
        "https://huggingface.co/blog/feed.xml",
        "Hugging Face",
        limit,
    )


# ============================================================
# ARTICLE PROCESSING
# ============================================================


def process_articles(articles, days, max_articles=None):
    """
    Filter, normalize, classify and deduplicate articles.

    - Validates publication dates.
    - Removes articles outside the requested period.
    - Deduplicates normalized titles.
    - Restores original trend classification.
    - Sorts newest first.
    - Optionally limits the returned articles.

    Does not mutate input articles.
    """

    validate_limits(days, max_articles)

    now = datetime.now(timezone.utc)
    cutoff = now - timedelta(days=days)

    unique = {}

    for article in articles:
        title = article.get("title", "")
        url = article.get("url", "")
        published = article.get("published_at")

        if not title or not url or not published:
            continue

        try:
            date = datetime.fromisoformat(str(published).replace("Z", "+00:00"))

            if date.tzinfo is None:
                date = date.replace(tzinfo=timezone.utc)

            date = date.astimezone(timezone.utc)

        except (TypeError, ValueError):
            continue

        # Reject outdated and future-dated articles.
        if not cutoff <= date <= now:
            continue

        # Normalize title for deduplication.
        key = re.sub(r"\W+", "", title.casefold())

        if not key:
            continue

        normalized = dict(article)

        normalized["published_at"] = date.isoformat()

        # Restore keyword-based trend classification.
        normalized["trends"] = classify_trends(normalized)

        previous = unique.get(key)

        # Keep the newest article when titles are duplicates.
        if previous is None or (
            normalized["published_at"],
            normalized["url"],
        ) > (
            previous["published_at"],
            previous["url"],
        ):
            unique[key] = normalized

    result = sorted(
        unique.values(),
        key=lambda article: (
            article["published_at"],
            article["url"],
        ),
        reverse=True,
    )

    if max_articles is not None:
        return result[:max_articles]

    return result


# ============================================================
# CONCURRENT ARTICLE COLLECTION
# ============================================================


def collect(
    topic,
    days,
    queries,
    selected_sources,
    max_articles=10,
):
    """
    Collect articles concurrently from selected sources.

    Returns:
        selected_articles:
            Articles selected after max_articles limiting.

        errors:
            Dictionary of errors with source/query context.

        total_articles:
            Total unique matching articles before truncation.

    No JSON or Markdown files are generated.
    """

    validate_limits(days, max_articles)

    feed_limit = get_feed_limit()

    if not isinstance(selected_sources, (list, tuple, set)):
        raise ValueError("selected_sources must be a list of source names")

    invalid_sources = set(selected_sources) - set(SOURCES)

    if invalid_sources or not selected_sources:
        raise ValueError(f"Invalid selected_sources: {sorted(invalid_sources)}")

    query_list = list(
        dict.fromkeys(
            query.strip()
            for query in queries
            if isinstance(query, str) and query.strip()
        )
    )[:2] or [topic]

    tasks = []

    # Build source-specific tasks.
    for source in dict.fromkeys(selected_sources):

        if source == "google_news":
            for query in query_list:
                tasks.append(
                    (
                        f"google_news:{query}",
                        search_google_news,
                        (query, days, feed_limit),
                    )
                )

        elif source == "hacker_news":
            for query in query_list:
                tasks.append(
                    (
                        f"hacker_news:{query}",
                        search_hacker_news,
                        (query, days, feed_limit),
                    )
                )

        elif source == "arxiv":
            tasks.append(
                (
                    "arxiv",
                    search_arxiv,
                    (feed_limit,),
                )
            )

        elif source == "hugging_face":
            tasks.append(
                (
                    "hugging_face",
                    search_hugging_face,
                    (feed_limit,),
                )
            )

    start = perf_counter()

    raw_articles = []
    errors = {}

    # Execute independent source requests concurrently.
    with ThreadPoolExecutor(max_workers=min(MAX_WORKERS, len(tasks))) as executor:

        pending = {
            executor.submit(function, *args): task_name
            for task_name, function, args in tasks
        }

        for future in as_completed(pending):
            task_name = pending[future]

            try:
                results = future.result()

                raw_articles.extend(results)

                logger.info(
                    "%s: %d articles",
                    task_name.partition(":")[0],
                    len(results),
                )

            except Exception as exc:
                # Include task/query to prevent error overwrites.
                errors[task_name] = str(exc)

                logger.warning(
                    "%s collection failed",
                    task_name.partition(":")[0],
                )

    # Process all collected articles before applying the limit.
    all_articles = process_articles(
        raw_articles,
        days,
    )

    # Preserve total count separately.
    total_articles = len(all_articles)

    # Apply API response and analysis limit.
    selected_articles = all_articles[:max_articles]

    elapsed = perf_counter() - start

    logger.info(
        "Collection completed in %.2fs; " "%d unique articles, %d returned",
        elapsed,
        total_articles,
        len(selected_articles),
    )

    return selected_articles, errors, total_articles
